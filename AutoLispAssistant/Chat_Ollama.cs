using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using OllamaSharp.Models.Exceptions;
using OllamaSharp.Tools;
using Microsoft.Extensions.AI;

namespace AutoLispAssistant
{
    class Chat_Ollama
    {
        HttpClient _httpClient;
        OllamaApiClient _ollama;
        Chat _chat;
        List<object> _tools;
        private string _lastSaveCode;
        private string _lastDefun;

        private readonly string FUNCTION_SAVE_LISP = "SaveLisp";
        private readonly string FUNCTION_EXECUTE_LISP = "ExecuteLISP";
        private readonly string ARGUMENT_LASTCODE = "lastcode";

        public Chat_Ollama(string model)
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("http://localhost:11434");
            _httpClient.Timeout = TimeSpan.FromSeconds(1200);
            _ollama = new OllamaApiClient(_httpClient, model);
            _chat = new Chat(_ollama, """
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
                """);

            _tools = [
                new Tool()
                {
                    Function = new Function
                    {
                        Description = "LISPコードを保存する",
                        Name = FUNCTION_SAVE_LISP,
                        Parameters = new Parameters
                        {
                            Properties = new Dictionary<string, Property>
                            {
                                [ARGUMENT_LASTCODE] = new()
                                {
                                    Type = "string",
                                    Description = "直前に生成したLISPのコード"
                                }
                            },
                            Required = [ARGUMENT_LASTCODE],
                        }
                    },
                    Type = "function"
                },
                new Tool()
                {
                    Function = new Function
                    {
                        Description = "LISPコードを実行する",
                        Name = FUNCTION_EXECUTE_LISP,
                        Parameters = new Parameters
                        {
                            Properties = new Dictionary<string, Property>
                            {
                                [ARGUMENT_LASTCODE] = new()
                                {
                                    Type = "string",
                                    Description = "直前に生成したLISPのコード"
                                }
                            },
                            Required = [ARGUMENT_LASTCODE],
                        }
                    },
                    Type = "function"
                }];
            _lastSaveCode = string.Empty;
            _lastDefun = string.Empty;
        }

        public string SaveLisp(string lastcode)
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
            return $"{savepath}に保存しました。";
        }

        public string ExecuteLISP(string lastcode)
        {
            if (!string.IsNullOrEmpty(_lastSaveCode))
            {
                OnStartRunLisp(new StartRunLispEventArgs { Code = _lastSaveCode, Defun = _lastDefun });
                return "最後に保存したLISPを実行しました。";
            }
            else
            {
                string temppath = WindowUtils.SaveLisp(lastcode, true);
                OnStartRunLisp(new StartRunLispEventArgs { Code = temppath, Defun = LispUtils.GetLispCommandName(lastcode) });
                return "直前に生成したLISPを実行しました。";
            }
        }

        public async Task<string> GetResponse(string prompt)
        {
            OnAssistantResponse(new AIResponseEventArgs { Text = $"\nAIアシスタント:\n" });
            string response = "";
            await foreach (var answerToken in _chat.SendAsync($"制約事項に従い、入力内容に対しての回答を作成する。\r\n入力内容 : {prompt}", _tools))
            {
                OnAssistantResponse(new AIResponseEventArgs { Text = answerToken });
                response += answerToken;
            }
            if(_chat.Messages.Last().ToolCalls.Count() > 0)
            {
                foreach (var toolCall in _chat.Messages.Last().ToolCalls)
                {
                    var keys = toolCall.Function.Arguments.Select(kvp => $"{kvp.Key}").ToList();
                    var values = toolCall.Function.Arguments.Select(kvp => $"{kvp.Value}").ToList();

                    if (toolCall.Function.Name == FUNCTION_SAVE_LISP && keys.First() == ARGUMENT_LASTCODE)
                    {
                        var result = SaveLisp(values.First());
                        OnAssistantResponse(new AIResponseEventArgs { Text = result });
                        response += result;
                    }
                    else if (toolCall.Function.Name == FUNCTION_EXECUTE_LISP && keys.First() == ARGUMENT_LASTCODE)
                    {
                        var result = ExecuteLISP(values.First());
                        OnAssistantResponse(new AIResponseEventArgs { Text = result });
                        response += result;
                    }
                }
            }
            else
            {
                if (LispUtils.CheckLispCode(response) && !string.IsNullOrEmpty(_lastSaveCode))
                {
                    _lastSaveCode = string.Empty;
                }
            }
            return response;
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
