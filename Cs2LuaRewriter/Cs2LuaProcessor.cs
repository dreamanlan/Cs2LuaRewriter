using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Xml;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTool
{
    public enum ExitCode : int
    {
        Success = 0,
        SyntaxError = 1,
        SemanticError = 2,
        FileNotFound = 3,
        Exception = 4,
    }
    public static class Cs2LuaProcessor
    {
        public static ExitCode Process(string srcFile, string outputDir, string rootNs, IList<string> macros, IList<string> undefMacros, IDictionary<string, string> _refByNames, IDictionary<string, string> _refByPaths, string systemDllPath, bool outputResult, bool parallel)
        {
            if (string.IsNullOrEmpty(outputDir)) {
                outputDir = "../rewrite";
            }

            List<string> preprocessors = new List<string>(macros);
            preprocessors.Add("__LUA__");

            string exepath = System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            string path = Path.GetDirectoryName(srcFile);
            string name = Path.GetFileNameWithoutExtension(srcFile);
            string ext = Path.GetExtension(srcFile);
            
            string logDir = Path.Combine(path, "log");
            if (!Directory.Exists(logDir)) {
                Directory.CreateDirectory(logDir);
            }
            if (!Path.IsPathRooted(outputDir)) {
                outputDir = Path.Combine(path, outputDir);
                outputDir = Path.GetFullPath(outputDir);
            }
            if (!Directory.Exists(outputDir)) {
                Directory.CreateDirectory(outputDir);
            }

            List<string> files = new List<string>();
            Dictionary<string, string> refByNames = new Dictionary<string, string>(_refByNames);
            Dictionary<string, string> refByPaths = new Dictionary<string, string>(_refByPaths);
            if (ext == ".csproj") {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(srcFile);
                var nodes = SelectNodes(xmlDoc, "ItemGroup", "Reference");
                foreach (XmlElement node in nodes) {
                    var aliasesNode = SelectSingleNode(node, "Aliases");
                    var pathNode = SelectSingleNode(node, "HintPath");
                    if (null != pathNode && !refByPaths.ContainsKey(pathNode.InnerText)) {
                        if (null != aliasesNode)
                            refByPaths.Add(pathNode.InnerText, aliasesNode.InnerText);
                        else
                            refByPaths.Add(pathNode.InnerText, "global");
                    } else {
                        string val = node.GetAttribute("Include");
                        if (!string.IsNullOrEmpty(val) && !refByNames.ContainsKey(val)) {
                            if (null != aliasesNode)
                                refByNames.Add(val, aliasesNode.InnerText);
                            else
                                refByNames.Add(val, "global");
                        }
                    }
                }
                string prjOutputDir = "bin/Debug/";
                nodes = SelectNodes(xmlDoc, "PropertyGroup");
                foreach (XmlElement node in nodes) {
                    string condition = node.GetAttribute("Condition");
                    var defNode = SelectSingleNode(node, "DefineConstants");
                    var pathNode = SelectSingleNode(node, "OutputPath");
                    if (null != defNode && null != pathNode) {
                        string text = defNode.InnerText.Trim();
                        if (condition.IndexOf("Debug") > 0 || condition.IndexOf("Release") < 0 && (text == "DEBUG" || text.IndexOf(";DEBUG;") > 0 || text.StartsWith("DEBUG;") || text.EndsWith(";DEBUG"))) {
                            preprocessors.AddRange(text.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                            prjOutputDir = pathNode.InnerText.Trim();
                            break;
                        }
                    }
                }
                nodes = SelectNodes(xmlDoc, "ItemGroup", "ProjectReference");
                foreach (XmlElement node in nodes) {
                    string val = node.GetAttribute("Include");
                    string prjFile = Path.Combine(path, val.Trim());
                    var nameNode = SelectSingleNode(node, "Name");
                    if (null != prjFile && null != nameNode) {
                        string prjName = nameNode.InnerText.Trim();
                        string prjOutputFile = ParseProjectOutputFile(prjFile, prjName);
                        string fileName = Path.Combine(prjOutputDir, prjOutputFile);
                        if (!refByPaths.ContainsKey(fileName)) {
                            refByPaths.Add(fileName, "global");
                        }
                    }
                }
                nodes = SelectNodes(xmlDoc, "ItemGroup", "Compile");
                foreach (XmlElement node in nodes) {
                    string val = node.GetAttribute("Include");
                    if (!string.IsNullOrEmpty(val) && val.EndsWith(".cs") && !files.Contains(val)) {
                        files.Add(val);
                    }
                }
            } else {
                files.Add(srcFile);
            }

            foreach(string m in undefMacros){
                preprocessors.Remove(m);
            }

            bool haveError = false;
            Dictionary<string, SyntaxTree> trees = new Dictionary<string, SyntaxTree>();
            using (StreamWriter sw = new StreamWriter(Path.Combine(logDir, "SyntaxError.log"))) {
                using (StreamWriter sw2 = new StreamWriter(Path.Combine(logDir, "SyntaxWarning.log"))) {
                    Action<string> handler = (file) => {
                        string filePath = Path.Combine(path, file);
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        CSharpParseOptions options = new CSharpParseOptions();
                        options = options.WithPreprocessorSymbols(preprocessors);
                        options = options.WithFeatures(new Dictionary<string, string> { { "IOperation", "true" } });
                        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), options, filePath);
                        lock (trees) {
                            trees.Add(file, tree);
                        }

                        var diags = tree.GetDiagnostics();
                        bool firstError = true;
                        bool firstWarning = true;
                        foreach (var diag in diags) {
                            if (diag.Severity == DiagnosticSeverity.Error) {
                                if (firstError) {
                                    LockWriteLine(sw, "============<<<Syntax Error:{0}>>>============", fileName);
                                    firstError = false;
                                }
                                string msg = diag.ToString();
                                LockWriteLine(sw, "{0}", msg);
                                haveError = true;
                            } else {
                                if (firstWarning) {
                                    LockWriteLine(sw2, "============<<<Syntax Warning:{0}>>>============", fileName);
                                    firstWarning = false;
                                }
                                string msg = diag.ToString();
                                LockWriteLine(sw2, "{0}", msg);
                            }
                        }
                    };
                    if (parallel) {
                        Parallel.ForEach(files, handler);
                    } else {
                        foreach (var file in files) {
                            handler(file);
                        }
                    }
                    sw2.Close();
                }
                sw.Close();
            }
            if (haveError) {
                Console.WriteLine("{0}", File.ReadAllText(Path.Combine(logDir, "SyntaxError.log")));
                return ExitCode.SyntaxError;
            }

            //确保常用的Assembly被引用
            if (!refByNames.ContainsKey("mscorlib")) {
                refByNames.Add("mscorlib", "global");
            }
            if (!refByNames.ContainsKey("System")) {
                refByNames.Add("System", "global");
            }
            if (!refByNames.ContainsKey("System.Core")) {
                refByNames.Add("System.Core", "global");
            }
            List<MetadataReference> refs = new List<MetadataReference>();
            if (string.IsNullOrEmpty(systemDllPath)) {
                if (ext == ".cs") {
                    refs.Add(MetadataReference.CreateFromFile(typeof(Queue<>).Assembly.Location));
                    refs.Add(MetadataReference.CreateFromFile(typeof(HashSet<>).Assembly.Location));
                }
                foreach (var pair in refByNames) {
#pragma warning disable 618
                    Assembly assembly = Assembly.LoadWithPartialName(pair.Key);
#pragma warning restore 618
                    if (null != assembly) {
                        var arr = System.Collections.Immutable.ImmutableArray.Create(pair.Value);
                        refs.Add(MetadataReference.CreateFromFile(assembly.Location, new MetadataReferenceProperties(MetadataImageKind.Assembly, arr)));
                    }
                }
            } else {
                foreach (var pair in refByNames) {
                    string file = Path.Combine(systemDllPath, pair.Key) + ".dll";
                    var arr = System.Collections.Immutable.ImmutableArray.Create(pair.Value);
                    refs.Add(MetadataReference.CreateFromFile(file, new MetadataReferenceProperties(MetadataImageKind.Assembly, arr)));
                }
            }

            foreach (var pair in refByPaths) {
                string fullPath = Path.Combine(path, pair.Key);
                var arr = System.Collections.Immutable.ImmutableArray.Create(pair.Value);
                refs.Add(MetadataReference.CreateFromFile(fullPath, new MetadataReferenceProperties(MetadataImageKind.Assembly, arr)));
            }

            bool haveSemanticError = false;
            StringBuilder errorBuilder = new StringBuilder();
            StringBuilder sb = new StringBuilder();
            CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            compilationOptions = compilationOptions.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);
            CSharpCompilation compilation = CSharpCompilation.Create(name);
            compilation = compilation.WithOptions(compilationOptions);
            compilation = compilation.AddReferences(refs.ToArray());
            compilation = compilation.AddSyntaxTrees(trees.Values);

            SymbolTable.Instance.Init(compilation, rootNs, exepath);

            using (StreamWriter sw = new StreamWriter(Path.Combine(logDir, "SemanticError.log"))) {
                using (StreamWriter sw2 = new StreamWriter(Path.Combine(logDir, "SemanticWarning.log"))) {
                    foreach (var pair in trees) {
                        var fileName = Path.Combine(path, pair.Key);
                        var filePath = Path.Combine(outputDir, pair.Key);
                        var dir = Path.GetDirectoryName(filePath);
                        if (!Directory.Exists(dir)) {
                            Directory.CreateDirectory(dir);
                        }
                        SyntaxTree tree = pair.Value;
                        var model = compilation.GetSemanticModel(tree, true);

                        var diags = model.GetDiagnostics();
                        bool firstError = true;
                        bool firstWarning = true;
                        foreach (var diag in diags) {
                            if (diag.Severity == DiagnosticSeverity.Error) {
                                if (firstError) {
                                    LockWriteLine(sw, "============<<<Semantic Error:{0}>>>============", fileName);
                                    firstError = false;
                                }
                                string msg = diag.ToString();
                                LockWriteLine(sw, "{0}", msg);
                                haveSemanticError = true;
                            } else {
                                if (firstWarning) {
                                    LockWriteLine(sw2, "============<<<Semantic Warning:{0}>>>============", fileName);
                                    firstWarning = false;
                                }
                                string msg = diag.ToString();
                                LockWriteLine(sw2, "{0}", msg);
                            }
                        }

                        Logger.Instance.ClearLog();
                        Cs2LuaRewriter rewriter = new Cs2LuaRewriter(rootNs, model);
                        var newRoot = rewriter.Visit(tree.GetRoot()) as CSharpSyntaxNode;
                        string txt = newRoot.ToFullString();
                        sb.AppendLine(txt);
                        File.WriteAllText(filePath, txt);

                        if (Logger.Instance.HaveError) {
                            errorBuilder.AppendLine(Logger.Instance.ErrorLog);
                        }
                    }
                }
            }

            File.WriteAllText(Path.Combine(logDir, "Cs2LuaError.log"), errorBuilder.ToString());
            using (var sw = new StreamWriter(Path.Combine(logDir, "IllegalGenericOrExtensions.txt"))) {
                sw.WriteLine("IllegalGenericTypes:");
                foreach (var type in SymbolTable.Instance.IllegalGenericTypes) {
                    sw.WriteLine("\t{0}", type);
                }
                sw.WriteLine("IllegalGenericMethods:");
                foreach (var method in SymbolTable.Instance.IllegalGenericMethods) {
                    sw.WriteLine("\t{0}", method);
                }
                sw.WriteLine("IllegalParameterGenericTypes:");
                foreach (var type in SymbolTable.Instance.IllegalParameterGenericTypes) {
                    sw.WriteLine("\t{0}", type);
                }
                sw.WriteLine("IllegalExtensions:");
                foreach (var type in SymbolTable.Instance.IllegalExtensions) {
                    sw.WriteLine("\t{0}", type);
                }
                sw.WriteLine("IllegalConvertions:");
                foreach (var pair in SymbolTable.Instance.IllegalConvertions) {
                    foreach (var type in pair.Value) {
                        sw.WriteLine("\t{0}=>{1}", pair.Key, type);
                    }
                }
                sw.WriteLine();
                sw.WriteLine("AccessMemberOfIllegalGenericTypes:");
                foreach (var type in SymbolTable.Instance.AccessMemberOfIllegalGenericTypes) {
                    sw.WriteLine("\t{0}", type);
                }
            }
            if (outputResult) {
                Console.Write(sb.ToString());
            }
            if (haveSemanticError) {
                return ExitCode.SemanticError;
            }
            return ExitCode.Success;
        }

        private static void Log(string file, string msg)
        {
            Console.WriteLine("[{0}]:{1}", file, msg);
        }
        
        private static void LockWriteLine(StreamWriter sw, string fmt, params object[] args)
        {
            lock (sw) {
                sw.WriteLine(fmt, args);
            }
        }

        private static string ParseProjectOutputFile(string srcFile, string prjName)
        {
            string fileName = prjName + ".dll";
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(srcFile);
            var nodes = SelectNodes(xmlDoc, "PropertyGroup");
            foreach (XmlElement node in nodes) {
                var typeNode = SelectSingleNode(node, "OutputType");
                var nameNode = SelectSingleNode(node, "AssemblyName");
                if (null != typeNode && null != nameNode) {
                    string type = typeNode.InnerText.Trim();
                    string name = nameNode.InnerText.Trim();
                    fileName = name + (type == "Library" ? ".dll" : ".exe");
                }
            }
            return fileName;
        }

        private static List<XmlElement> SelectNodes(XmlNode node, params string[] names)
        {
            return SelectNodesRecursively(node, 0, names);
        }
        private static List<XmlElement> SelectNodesRecursively(XmlNode node, int index, params string[] names)
        {
            string name = names[index];
            List<XmlElement> list = new List<XmlElement>();
            foreach (var cnode in node.ChildNodes) {
                var element = cnode as XmlElement;
                if (null != element) {
                    if (element.Name == name) {
                        if (index < names.Length - 1) {
                            list.AddRange(SelectNodesRecursively(element, index + 1, names));
                        } else {
                            list.Add(element);
                        }
                    } else if (index == 0) {
                        list.AddRange(SelectNodesRecursively(element, index, names));
                    }
                }
            }
            return list;
        }
        private static XmlElement SelectSingleNode(XmlNode node, params string[] names)
        {
            return SelectSingleNodeRecursively(node, 0, names);
        }
        private static XmlElement SelectSingleNodeRecursively(XmlNode node, int index, params string[] names)
        {
            XmlElement ret = null;
            string name = names[index];
            foreach (var cnode in node.ChildNodes) {
                var element = cnode as XmlElement;
                if (null != element) {
                    if (element.Name == name) {
                        if (index < names.Length - 1) {
                            ret = SelectSingleNodeRecursively(element, index + 1, names);
                        } else {
                            ret = element;
                        }
                    } else if (index == 0) {
                        ret = SelectSingleNodeRecursively(element, index, names);
                    }
                    if (null != ret) {
                        break;
                    }
                }
            }
            return ret;
        }
    }
}
