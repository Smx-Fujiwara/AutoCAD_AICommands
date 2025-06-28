using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.ComponentModel;

using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Audio;

namespace AutoLispAssistant
{
    public class Chat_OpenAI
    {
        private OpenAIClient _openAIClient;
        private IChatClient _chatClient;
        private AudioClient _audioClient;
        private List<ChatMessage> _chatHistory;
        private ChatOptions _chatOptions;
        private string _lastSaveCode;
        private string _lastDefun;
        public Chat_OpenAI(string key, string model)
        {
            _openAIClient = new OpenAIClient(key);
            _chatClient = new ChatClientBuilder(_openAIClient.GetChatClient(model).AsIChatClient()).UseFunctionInvocation().Build();
            _audioClient = _openAIClient.GetAudioClient("whisper-1");
            _chatHistory = new()
            {
                new ChatMessage(ChatRole.System, """
                    あなたはAutoCADのパワーユーザーであり、特にAutoLISPに精通しています。
                    あなたは質問された内容から適切なLISPのコードを生成や、コードの保存と実行などを支援するアシスタントで以下の業務を遂行します。
                    - 質問された内容をAutoCAD上での実行したいことと想定し、LISPコードを生成します
                    - ユーザーからLISPの保存を依頼された場合、生成したLISPコードを保存します
                    - ユーザーからLISPの保存を依頼された場合、生成したLISPコードや、保存したLISPコードを実行します

                    最初に挨拶と自己紹介をした後に、AutoCADで何をしたいのかを尋ねます。
                    LISPコードを提案する際に、ダイアログボックスを表示するなど、入力だけで完結しないプロンプトの場合は、その旨も説明します。
                    回答の最後には、他に手伝えることがないか尋ねること。

                    #制約事項
                    - フォーマットの再有効化 - コード出力はマークダウンでラップされるべきである
                    - (defun)を使わずに、ロード直後に実行されるコードを提供する。LISPコードをロードすることで即座に実行されることを前提とする。このルールは厳密かつ妥協なく守られる。
                    - Visual LISPを使用しなくても実現可能であれば、極力Visual LISPを使用しない。
                    - 図形やスタイルの作成は、可能な限り(command)は使用しない。
                """)
            };
            _chatOptions = new ChatOptions
            {
                Tools =
                            [
                                AIFunctionFactory.Create(SaveLisp),
                                AIFunctionFactory.Create(ExecuteLISP)
                            ]
            };
            _lastSaveCode = string.Empty;
            _lastDefun = string.Empty;
        }

        [Description("LISPコードを保存する")]
        public string SaveLisp([Description("直前に生成したLISPのコード")] string lastcode)
        {
            if (string.IsNullOrEmpty(lastcode))
            {
                return "LISPの保存に失敗しました。";
            }
            string savepath = WindowUtils.SaveLisp(lastcode);
            if (string.IsNullOrEmpty(savepath))
            {
                return "LISPの保存に失敗しました。";
            }
            _lastSaveCode = savepath;
            _lastDefun = LispUtils.GetLispCommandName(lastcode);
            return $"{savepath}";
        }

        [Description("LISPコードを実行する")]
        public string ExecuteLISP([Description("直前に生成したLISPのコード")] string lastcode)
        {
            if(!string.IsNullOrEmpty(_lastSaveCode))
            {
                OnStartRunLisp(new StartRunLispEventArgs { Code = _lastSaveCode, Defun = _lastDefun });
                return "最後に保存したLISPを実行";
            }
            else
            {
                string temppath = WindowUtils.SaveLisp(lastcode, true);
                OnStartRunLisp(new StartRunLispEventArgs { Code = temppath, Defun = LispUtils.GetLispCommandName(lastcode) });
                return "直前に生成したLISPを実行";
            }
        }

        public async Task<string> GetResponse(string prompt)
        {
            _chatHistory.Add(new ChatMessage(ChatRole.User, $"制約事項に従い、入力内容に対しての回答を作成する。\r\n入力内容 : {prompt}"));
            OnAssistantResponse(new AIResponseEventArgs { Text = $"\nAIアシスタント:\n" });
            string response = "";
            await foreach (var item in _chatClient.GetStreamingResponseAsync(_chatHistory, _chatOptions))
            {
                OnAssistantResponse(new AIResponseEventArgs { Text = item.Text });
                response += item.Text;
            }
            _chatHistory.Add(new ChatMessage(ChatRole.Assistant, response));
            if(LispUtils.CheckLispCode(response) && !string.IsNullOrEmpty(_lastSaveCode))
            {
                _lastSaveCode = string.Empty;
            }
            return response;
        }

        public async Task<string> SpeechToText(Stream audio)
        {
            try
            {
                var res = await _audioClient.TranscribeAudioAsync(audio, "recognized_audio.wav");
                return res.Value.Text;
            }
            catch
            {
                return string.Empty;
            }
        }

        protected virtual void OnAssistantResponse(AIResponseEventArgs e)
        {
            EventHandler<AIResponseEventArgs> handler = AssistantResponse;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        public event EventHandler<AIResponseEventArgs> AssistantResponse;

        protected virtual void OnStartRunLisp(StartRunLispEventArgs e)
        {
            EventHandler<StartRunLispEventArgs> handler = StartRunLisp;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        public event EventHandler<StartRunLispEventArgs> StartRunLisp;
    }
}
