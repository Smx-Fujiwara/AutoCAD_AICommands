using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;

#pragma warning disable SKEXP0070
namespace AutoLispAssistant
{
    class Chat_Google
    {
        private IKernelBuilder _builder;
        private Kernel _kernel;
        private ChatHistory _chatHistory;
        private GeminiPromptExecutionSettings _settings;
        IChatCompletionService _chatCompletionService;

        static Chat_Google _chat;

        public Chat_Google(string key, string model)
        {
            _chat = this;
            _builder = Kernel.CreateBuilder();
            _builder.Plugins.AddFromType<LispPlugin>();
            _kernel = _builder.AddGoogleAIGeminiChatCompletion(
                    modelId: model,
                    apiKey: key)
                .Build();
            _chatHistory = new ChatHistory(""""
                    あなたはAutoCADのパワーユーザーであり、特にAutoLISPに精通しています。
                    あなたは質問された内容から適切なLISPのコードを生成や、コードの保存と実行などを支援するアシスタントで以下の業務を遂行します。
                    - 質問された内容をAutoCAD上での実行したいことと想定し、LISPコードを生成します
                    - ユーザーからLISPの保存を依頼された場合、提案したLISPコードを保存する関数を実行する LispSavePlugin
                    - ユーザーからLISPの実行を依頼された場合、提案したLISPコードや、保存したLISPコードを実行します

                    最初に挨拶と自己紹介をした後に、AutoCADで何をしたいのかを尋ねます。
                    LISPコードを提案する際に、ダイアログボックスを表示するなど、入力だけで完結しないプロンプトの場合は、その旨も説明します。
                    回答の最後には、他に手伝えることがないか尋ねること。

                    #制約事項
                    - (defun)を使わずに、ロード直後に実行されるコードを提供する。LISPコードをロードすることで即座に実行されることを前提とする。このルールは厳密かつ妥協なく守られる。
                    - Visual LISPを使用しなくても実現可能であれば、極力Visual LISPを使用しない。
                    - 図形やスタイルの作成は、可能な限り(command)は使用しない。
                """", AuthorRole.User);
            _settings = new()
            {
                MaxTokens = 1000,
                ToolCallBehavior = GeminiToolCallBehavior.AutoInvokeKernelFunctions
            };
            _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
        }

        public async Task<string> GetResponse(string prompt)
        {
            _chatHistory.AddUserMessage($"制約事項に従い、入力内容に対しての回答を作成する。\r\n入力内容 : {prompt}");
            OnAssistantResponse(new AIResponseEventArgs { Text = $"\nAIアシスタント:\n" });
            string response = "";
            await foreach (var item in _chatCompletionService.GetStreamingChatMessageContentsAsync(_chatHistory, _settings, _kernel))
            {
                OnAssistantResponse(new AIResponseEventArgs { Text = item.Content });
                response += item.Content;
            }
            _chatHistory.AddAssistantMessage(response);
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

        public virtual void OnStartRunLisp(StartRunLispEventArgs e)
        {
            EventHandler<StartRunLispEventArgs> handler = StartRunLisp;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        public event EventHandler<StartRunLispEventArgs> StartRunLisp;

        public class LispPlugin
        {
            private string _lastSaveCode = string.Empty;
            private string _lastDefun = string.Empty;
            [KernelFunction, Description("""
                LISPコードを保存する。
                保存先の指定や、保存するコードの取得は関数内で実行される。
                直前に生成したコードをlspファイル保存する。
                Save lisp code.
                Save the last generated code to .lsp.
                """)]
            public string SaveLisp([Description("直前に生成したコード")] string lastcode)
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

            [KernelFunction, Description("""
                LISPコードを実行する。
                LISPコードが保存されている場合はそのコードを、
                LISPコードが保存されていない、または、保存後に新しいコードを生成していた場合、
                直前に生成したLISPコードを一時的に保存して実行する。
                LISPコードの一時的な保存は関数内で実行される。
                Execute lisp code.
                If the lisp code is saved, execute the saved code.
                If the lisp code is not saved, or if a new code is generated after saving,
                execute the last generated lisp code by temporarily saving it.
                """)]
            public string ExecuteLisp([Description("直前に生成したコード")] string lastcode)
            {
                if (!string.IsNullOrEmpty(_lastSaveCode) && _lastSaveCode == lastcode)
                {
                    _chat?.OnStartRunLisp(new StartRunLispEventArgs { Code = _lastSaveCode, Defun = _lastDefun });
                    return "最後に保存したLISPを実行";
                }
                else
                {
                    string temppath = WindowUtils.SaveLisp(lastcode, true);
                    _chat?.OnStartRunLisp(new StartRunLispEventArgs { Code = temppath, Defun = LispUtils.GetLispCommandName(lastcode) });
                    return "直前に生成したLISPを実行";
                }
            }
        }
    }
}
#pragma warning restore SKEXP0070
