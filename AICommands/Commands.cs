
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Speech.Recognition;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AICommands
{
    public class Commands : IExtensionApplication
    {
        private const string OPENAI = "OpenAI";
        private const string GEMINI = "Gemini";
        private const string OLLAMA = "Ollama";
        private static Configuration _config = null;

        public void Initialize()
        {
            // ファイルから再読み込み
            string jsonFromFile = File.ReadAllText("./config.json");
            _config = JsonSerializer.Deserialize<Configuration>(jsonFromFile);
        }
        public void Terminate()
        {
            File.WriteAllText("./config.json", JsonSerializer.Serialize(_config));
        }

        [CommandMethod("CHATOPTION")]
        public void CHATOPTION()
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var opt = new PromptIntegerOptions("\nAIアシスタントの選択 (0: OpenAI, 1: Google, 2: Ollama)");
            opt.DefaultValue = _config.AI switch
            {
                OPENAI => 0,
                GEMINI => 1,
                OLLAMA => 2,
                _ => 0
            };

            opt.AllowNegative = false;
            opt.LowerLimit = 0;
            opt.UpperLimit = 2;
            var res = ed.GetInteger(opt);
            if(res.Status != PromptStatus.OK)
            {
                return;
            }
            _config.AI = res.Value switch
            {
                0 => OPENAI,
                1 => GEMINI,
                2 => OLLAMA,
                _ => OPENAI
            };
        }

        private Assembly? _assistantAssembly = null;
        private Type? _assistantType = null;
        private object? _assistantInstance = null;
        private bool _isBoot = false;
        private static bool _isPause = false;
        private static string _lisp = string.Empty;
        private static string _defun = string.Empty;
        private static int _lispEventCount = 0;
        private SpeechRecognitionEngine _recognizer = null;
        private MemoryStream _memoryStream = null;

        [CommandMethod("CHATSAMPLE", CommandFlags.Session)]
        public void CHATSAMPLE()
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if(_isPause)
            {
                Utils.WriteMessage("\n***** AIアシスタントを再開 *****\n");
                _isPause = false;
            }
            else
            {
                if (_isBoot)
                {
                    Utils.WriteMessage("\n***** AIアシスタントは既に起動しています *****\n");
                    return;
                }
                else
                {
                    Utils.WriteMessage("\n***** AIアシスタントを起動 *****\n");
                    _isBoot = true;
                }
            }

            if (_assistantAssembly == null)
            {
                var path = Path.GetDirectoryName(Assembly.GetAssembly(this.GetType()).Location);
                var assistantContext = new CustomAssemblyLoadContext(path);
                _assistantAssembly = assistantContext.LoadFromAssemblyPath(Path.Combine(path, "AutoLispAssistant.dll"));
                _assistantAssembly.GetType();
                _assistantType = _assistantAssembly.GetType("AutoLispAssistant.Assistant");
                _assistantInstance = Activator.CreateInstance(_assistantType);
                var initialize = _assistantType?.GetMethod("Initialize");
                switch(_config.AI)
                {
                    case GEMINI:
                        // Google
                        initialize?.Invoke(_assistantInstance, new object[] {
                            1,
                            "GeminiKey",
                            _config.Gemini.Model
                        });
                        break;
                    case OLLAMA:
                        // Ollama
                        initialize?.Invoke(_assistantInstance, new object[] {
                            2,
                            "",
                            _config.Ollama.Model
                        });
                        break;
                    case OPENAI:
                    default:
                        // OpenAI
                        initialize?.Invoke(_assistantInstance, new object[] { 
                            0, 
                            "OpenAIKey",
                            _config.OpenAI.Model
                        });
                        break;
                }
                var assistantResponseEventInfo = _assistantType.GetEvent("AssistantResponse");
                var assistantResponseEventHandlerMethod = typeof(Commands).GetMethod(nameof(OnAssistantResponse), BindingFlags.Static | BindingFlags.NonPublic);
                var assistantResponseEventHandler = Delegate.CreateDelegate(assistantResponseEventInfo.EventHandlerType, null, assistantResponseEventHandlerMethod);
                assistantResponseEventInfo.AddEventHandler(_assistantInstance, assistantResponseEventHandler);
                var startRunLispEventInfo = _assistantType.GetEvent("StartRunLisp");
                var startRunLispEventHandlerMethod = typeof(Commands).GetMethod(nameof(OnStartRunLisp), BindingFlags.Static | BindingFlags.NonPublic);
                var startRunLispEventHandler = Delegate.CreateDelegate(startRunLispEventInfo.EventHandlerType, null, startRunLispEventHandlerMethod);
                startRunLispEventInfo.AddEventHandler(_assistantInstance, startRunLispEventHandler);
            }
            ChatStart();
        }

        private static void OnAssistantResponse(object sender, object e)
        {
            var property = e.GetType().GetProperty("Text");
            var text = property?.GetValue(e)?.ToString();
            Utils.WriteMessage(text);
        }

        private static void OnStartRunLisp(object sender, object e)
        {
            _isPause = true;
            var property = e.GetType().GetProperty("Code");
            _lisp = property?.GetValue(e)?.ToString();
            property = e.GetType().GetProperty("Defun");
            _defun = property?.GetValue(e)?.ToString();
        }

        public async void ChatStart()
        {
            while (true)
            {
                var doc = CadApp.DocumentManager.MdiActiveDocument;
                using DocumentLock documentLock = doc.LockDocument();
                var ed = doc.Editor;

                PromptResult res = null;
                PromptStringOptions opt = null;
                switch (_config.AI)
                {
                    case GEMINI:
                    case OLLAMA:
                        // Google
                        // Ollama
                        opt = new PromptStringOptions("\n質問を入力してください (ExitまたはEscキーで終了)");
                        opt.AllowSpaces = true;
                        res = ed.GetString(opt);

                        if (res.Status == PromptStatus.OK)
                        {
                            if ("EXIT".IndexOf(res.StringResult.ToUpper()) == 0)
                            {
                                Utils.WriteMessage("\n***** AIアシスタントを終了 *****\n");
                                _isBoot = false;
                                return;
                            }

                        }
                        else
                        {
                            Utils.WriteMessage("\n***** AIアシスタントを終了 *****\n");
                            _isBoot = false;
                            return;
                        }
                        break;
                    default:
                        // OpenAI
                        opt = new PromptStringOptions("\n質問を入力してください または [音声入力(V)] (ExitまたはEscキーで終了)");
                        opt.AllowSpaces = true;
                        res = ed.GetString(opt);

                        if (res.Status == PromptStatus.OK)
                        {
                            if ("VOICE".IndexOf(res.StringResult.ToUpper()) == 0)
                            {
                                ed.PromptingForString += Ed_PromptingForString;
                                ed.PromptedForString += Ed_PromptedForString;
                                res = ed.GetString("\n質問を入力してください(ExitまたはEscキーで終了): ");
                                ed.PromptingForString -= Ed_PromptingForString;
                                ed.PromptedForString -= Ed_PromptedForString; ;
                            }
                            else if ("EXIT".IndexOf(res.StringResult.ToUpper()) == 0)
                            {
                                Utils.WriteMessage("\n***** AIアシスタントを終了 *****\n");
                                _isBoot = false;
                                return;
                            }

                        }
                        if (res.Status != PromptStatus.OK)
                        {
                            Utils.WriteMessage("\n***** AIアシスタントを終了 *****\n");
                            _isBoot = false;
                            return;
                        }
                        break;
                }
                if(res == null)
                {
                    Utils.WriteMessage("\n***** AIアシスタントを終了 *****\n");
                    _isBoot = false;
                    return;
                }

                var method = _assistantType?.GetMethod("GetResponse");
                var response = await (Task<string>)method?.Invoke(_assistantInstance, new object[] { res.StringResult });

                if (_isPause)
                {
                    Utils.WriteMessage("\n***** AIアシスタントを中断 *****\n");
                    ExecuteLISP(_lisp, _defun);
                    return;
                }
            }
        }

        private void Ed_PromptingForString(object sender, PromptStringOptionsEventArgs e)
        {
            _recognizer = new SpeechRecognitionEngine(new CultureInfo("ja-JP"));
            _recognizer.LoadGrammar(new DictationGrammar());
            _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
            _recognizer.SetInputToDefaultAudioDevice();

            _recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(2);
            _recognizer.BabbleTimeout = TimeSpan.FromSeconds(1);
            _recognizer.EndSilenceTimeout = TimeSpan.FromSeconds(0.5);
            _recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(0.5);

            _recognizer.RecognizeAsync(RecognizeMode.Multiple);
        }
        private void Recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            RecognizedAudio audio = e.Result.Audio;
            if (audio == null || audio.Duration.TotalSeconds == 0)
            {
                return;
            }

            _memoryStream = new MemoryStream();
            audio.WriteToWaveStream(_memoryStream);

            _memoryStream.Flush();
            _memoryStream.Position = 0;
            TextToSend();
        }

        public async void TextToSend()
        {
            var method = _assistantType?.GetMethod("SpeechToText");
            var text = await (Task<string>)method?.Invoke(_assistantInstance, new object[] { _memoryStream });
            SendKeys.Send(text + "\n");
            _memoryStream.Dispose();
            _memoryStream = null;
        }

        private void Ed_PromptedForString(object sender, PromptStringResultEventArgs e)
        {
            _recognizer.Dispose();
            _recognizer = null;
        }

        public void ExecuteLISP(string code, string defun)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute($@"(load ""{code.Replace(@"\", @"\\")}"") ", true, false, false);
            _lispEventCount++;
            if (!string.IsNullOrEmpty(defun))
            {
                _lispEventCount++;
                doc?.SendStringToExecute($"{defun} ", true, false, false);
            }
            doc.LispEnded += Doc_LispEnded;
            doc.LispCancelled += Doc_LispCancelled;
        }

        private void Doc_LispCancelled(object sender, EventArgs e)
        {
            var doc = (Document)sender;
            _lispEventCount--;
            if (_lispEventCount == 0)
            {
                doc.LispEnded -= Doc_LispEnded;
                doc.LispCancelled -= Doc_LispCancelled;
                doc?.SendStringToExecute($"CHATSAMPLE ", true, false, false);
            }
        }

        private void Doc_LispEnded(object sender, EventArgs e)
        {
            var doc = (Document)sender;
            _lispEventCount--;
            if(_lispEventCount == 0)
            {
                doc.LispEnded -= Doc_LispEnded;
                doc.LispCancelled -= Doc_LispCancelled;
                doc?.SendStringToExecute($"CHATSAMPLE ", true, false, false);
            }
        }


        public class CustomAssemblyLoadContext : AssemblyLoadContext
        {
            private readonly string _pluginPath;
            private readonly AssemblyDependencyResolver _resolver; 

            public CustomAssemblyLoadContext(string pluginPath) : base(isCollectible: true)
            {
                _pluginPath = pluginPath;
                _resolver = new AssemblyDependencyResolver(pluginPath);
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                string assemblyPath;
                // System.Text.Json の特定のバージョンをロード
                if (assemblyName.Name == "System.Text.Json" && assemblyName.Version >= new Version(9, 0, 0))
                {
                    assemblyPath = System.IO.Path.Combine(_pluginPath, "System.Text.Json.dll");
                    return LoadFromAssemblyPath(assemblyPath);
                }
                assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
                if (assemblyPath != null)
                {
                    return LoadFromAssemblyPath(assemblyPath);
                }
                else
                {
                    assemblyPath = System.IO.Path.Combine(_pluginPath, assemblyName.Name + ".dll");
                    if(System.IO.File.Exists(assemblyPath))
                    {
                        return LoadFromAssemblyPath(assemblyPath);
                    }
                }
                // 他のアセンブリはデフォルトのコンテキストでロード
                return null;
            }
        }

        public class Utils
        {
            public static void WriteMessage(string message)
            {
                var doc = CadApp.DocumentManager.MdiActiveDocument;
                using DocumentLock documentLock = doc.LockDocument();
                doc.Editor.WriteMessage(message);
            }
        }

        public class AIModelConfiguration
        {
            [JsonPropertyName("Model")]
            public string Model { get; set; }

            [JsonPropertyName("Key")]
            public string Key { get; set; }
        }

        public class Configuration
        {
            [JsonPropertyName("AI")]
            public string AI { get; set; }

            [JsonPropertyName("OpenAI")]
            public AIModelConfiguration OpenAI { get; set; }

            [JsonPropertyName("Gemini")]
            public AIModelConfiguration Gemini { get; set; }

            [JsonPropertyName("Ollama")]
            public AIModelConfiguration Ollama { get; set; }
        }
    }
}
