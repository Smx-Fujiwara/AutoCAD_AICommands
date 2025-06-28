using System.Threading.Tasks.Dataflow;

namespace AutoLispAssistant
{
    public enum AssistantType
    {
        OpenAI,
        Google,
        Ollama
    }
    public class AIResponseEventArgs : EventArgs
    {
        public string Text { get; set; }
    }
    public class StartRunLispEventArgs : EventArgs
    {
        public string Code { get; set; }
        public string Defun { get; set; }
    }

    public class Assistant
    {
        private AssistantType _assistantType;
        private Chat_OpenAI? _chatOpenAI = null;
        private Chat_Google? _chatGoogle = null;
        private Chat_Ollama? _chatOllama = null;

        public void Initialize(int type, string key, string model)
        {
            // 初期化処理
            _assistantType = (AssistantType)type;
            switch (_assistantType)
            {
                case AssistantType.Google:
                    // Initialize Google Assistant
                    _chatGoogle = new Chat_Google(key, model);
                    _chatGoogle.AssistantResponse += (sender, e) =>
                    {
                        OnAssistantResponse(e);
                    };
                    _chatGoogle.StartRunLisp += (sender, e) =>
                    {
                        OnStartRunLisp(e);
                    };
                    break;
                case AssistantType.Ollama:
                    // Initialize Ollama Assistant
                    _chatOllama = new Chat_Ollama(model);
                    _chatOllama.AssistantResponse += (sender, e) =>
                    {
                        OnAssistantResponse(e);
                    };
                    _chatOllama.StartRunLisp += (sender, e) =>
                    {
                        OnStartRunLisp(e);
                    };
                    break;
                default:
                    // Initialize OpenAI Assistant
                    _chatOpenAI = new Chat_OpenAI(key, model);
                    _chatOpenAI.AssistantResponse += (sender, e) =>
                    {
                        OnAssistantResponse(e);
                    };
                    _chatOpenAI.StartRunLisp += (sender, e) =>
                    {
                        OnStartRunLisp(e);
                    };
                    break;
            }
        }

        public async Task<string> GetResponse(string prompt)
        {
            var response = _assistantType switch
            {
                AssistantType.OpenAI => await _chatOpenAI?.GetResponse(prompt),
                AssistantType.Google => await _chatGoogle?.GetResponse(prompt),
                AssistantType.Ollama => await _chatOllama?.GetResponse(prompt),
                _ => string.Empty
            };
            return response;
        }

        public async Task<string> SpeechToText(Stream audio)
        {
            if(audio == null)
            {
                return string.Empty;
            }

            var text = _assistantType switch
            {
                AssistantType.OpenAI => await _chatOpenAI?.SpeechToText(audio),
                AssistantType.Google => string.Empty,
                AssistantType.Ollama => string.Empty,
                _ => string.Empty
            };
            return text;
        }

        public virtual void OnAssistantResponse(AIResponseEventArgs e)
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

    }

    public class WindowUtils
    {
        public static string SaveLisp(string code, bool temp = false)
        {
            if(temp)
            {
                string path = System.IO.Path.GetTempPath() + "tmpAIGenLisp.lsp";
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
                System.IO.File.WriteAllText(path, code);
                return path;
            }
            else
            {
                var result = string.Empty; // 保存されたファイル名を格納する変数
                Thread staThread = new Thread(() =>
                {
                    var sfd = new SaveFileDialog
                    {
                        Filter = "Lisp File|*.lsp",
                        Title = "Save Lisp File"
                    };

                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        // ファイルを書き込む
                        System.IO.File.WriteAllText(sfd.FileName, code);
                        result = sfd.FileName; // ファイル名を取得
                    }
                });

                staThread.SetApartmentState(ApartmentState.STA); // スレッドを STA モードに設定
                staThread.Start(); // スレッド開始
                staThread.Join();  // スレッドの終了を待機

                return result; // 保存されたファイル名を返す
            }
        }
    }

    public class LispUtils
    {
        public static string GetLispCommandName(string code)
        {
            if (code.Contains("(defun"))
            {
                int start = code.IndexOf("c:") + 2;
                int end = code.IndexOf(" ()", start);
                return code.Substring(start, end - start);
            }
            return string.Empty;
        }

        public static bool CheckLispCode(string code)
        {
            return code.Contains("```lisp");
        }
    }
}
