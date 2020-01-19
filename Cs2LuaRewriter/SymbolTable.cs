using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace RoslynTool
{
    internal class SymbolTable
    {
        internal CSharpCompilation Compilation
        {
            get { return m_Compilation; }
        }
        internal IAssemblySymbol AssemblySymbol
        {
            get { return m_AssemblySymbol; }
        }
        internal HashSet<string> Namespaces
        {
            get { return m_Namespaces; }
        }
        internal HashSet<string> IllegalGenericTypes
        {
            get { return m_IllegalGenericTypes; }
        }
        internal HashSet<string> IllegalGenericMethods
        {
            get { return m_IllegalGenericMethods; }
        }
        internal HashSet<string> IllegalParameterGenericTypes
        {
            get { return m_IllegalParameterGenericTypes; }
        }
        internal HashSet<string> IllegalExtensions
        {
            get { return m_IllegalExtensions; }
        }
        internal Dictionary<string, HashSet<string>> IllegalConvertions
        {
            get { return m_IllegalConvertions; }
        }
        internal HashSet<string> AccessMemberOfIllegalGenericTypes
        {
            get { return m_AccessMemberOfIllegalGenericTypes; }
        }
        internal void Init(CSharpCompilation compilation, string rootNs, string cfgPath)
        {
            m_Compilation = compilation;
            m_AssemblySymbol = compilation.Assembly;
            INamespaceSymbol nssym = m_AssemblySymbol.GlobalNamespace;
            InitRecursively(rootNs, nssym);

            Dsl.DslFile dslFile = new Dsl.DslFile();
            if (dslFile.Load(Path.Combine(cfgPath, "rewriter.dsl"), (msg) => { Console.WriteLine(msg); })) {
                foreach (var info in dslFile.DslInfos) {
                    var call = info.First.Call;
                    var fid = info.GetId();
                    if (fid != "config")
                        continue;
                    var cid = call.GetParamId(0);
                    if (cid == "LegalGenericTypeList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "type") {
                                    var v1 = cd.GetParamId(0);
                                    if (!m_LegalGenericTypes.Contains(v1)) {
                                        m_LegalGenericTypes.Add(v1);
                                    }
                                }
                            }
                        }
                    } else if (cid == "LegalGenericMethodList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "method") {
                                    var v1 = cd.GetParamId(0);
                                    var v2 = cd.GetParamId(0);
                                    var v = string.Format("{0}.{1}", v1, v2);
                                    if (!m_LegalGenericMethods.Contains(v)) {
                                        m_LegalGenericMethods.Add(v);
                                    }
                                }
                            }
                        }
                    } else if (cid == "LegalParameterGenericTypeList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "type") {
                                    var v1 = cd.GetParamId(0);
                                    if (!m_LegalParameterGenericTypes.Contains(v1)) {
                                        m_LegalParameterGenericTypes.Add(v1);
                                    }
                                }
                            }
                        }
                    } else if (cid == "LegalExtensionList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "type") {
                                    var v1 = cd.GetParamId(0);
                                    if (!m_LegalExtensions.Contains(v1)) {
                                        m_LegalExtensions.Add(v1);
                                    }
                                }
                            }
                        }
                    }
                    else if (cid == "LegalConvertionList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "convertion") {
                                    var v1 = cd.GetParamId(0);
                                    var v2 = cd.GetParamId(1);
                                    HashSet<string> targets;
                                    if(!m_LegalConvertions.TryGetValue(v1, out targets)) {
                                        targets = new HashSet<string>();
                                        m_LegalConvertions.Add(v1, targets);
                                    }
                                    if (!targets.Contains(v2)) {
                                        targets.Add(v2);
                                    }
                                }
                            }
                        }
                    } else if (cid == "IllegalTypeList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "type") {
                                    var v1 = cd.GetParamId(0);
                                    if (!m_IllegalTypes.Contains(v1)) {
                                        m_IllegalTypes.Add(v1);
                                    }
                                }
                            }
                        }
                    } else if (cid == "IllegalMethodList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "method") {
                                    var v1 = cd.GetParamId(0);
                                    var v2 = cd.GetParamId(0);
                                    var v = string.Format("{0}.{1}", v1, v2);
                                    if (!m_IllegalMethods.Contains(v)) {
                                        m_IllegalMethods.Add(v);
                                    }
                                }
                            }
                        }
                    } else if (cid == "IllegalPropertyList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "property") {
                                    var v1 = cd.GetParamId(0);
                                    var v2 = cd.GetParamId(0);
                                    var v = string.Format("{0}.{1}", v1, v2);
                                    if (!m_IllegalProperties.Contains(v)) {
                                        m_IllegalProperties.Add(v);
                                    }
                                }
                            }
                        }
                    } else if (cid == "IllegalFieldList") {
                        foreach (var comp in info.First.Statements) {
                            var cd = comp as Dsl.CallData;
                            if (null != cd) {
                                var mid = cd.GetId();
                                if (mid == "field") {
                                    var v1 = cd.GetParamId(0);
                                    var v2 = cd.GetParamId(0);
                                    var v = string.Format("{0}.{1}", v1, v2);
                                    if (!m_IllegalFields.Contains(v)) {
                                        m_IllegalFields.Add(v);
                                    }
                                }
                            }
                        }
                    }
                }
            }            

        }
        internal bool IsExternSymbol(ISymbol sym)
        {
            if (sym.Kind == SymbolKind.Method) {
                return IsExternSymbol(sym as IMethodSymbol);
            } else if (sym.Kind == SymbolKind.Field) {
                return IsExternSymbol(sym as IFieldSymbol);
            } else if (sym.Kind == SymbolKind.Property) {
                return IsExternSymbol(sym as IPropertySymbol);
            } else if (sym.Kind == SymbolKind.Event) {
                return IsExternSymbol(sym as IEventSymbol);
            } else {
                var arrSym = sym as IArrayTypeSymbol;
                if (null != arrSym) {
                    return IsExternSymbol(arrSym.ElementType);
                } else {
                    var typeSym = sym as ITypeSymbol;
                    if (null != typeSym) {
                        return IsExternSymbol(typeSym);
                    } else {
                        return sym.ContainingAssembly != m_AssemblySymbol;
                    }
                }
            }
        }
        internal bool IsExternSymbol(IMethodSymbol sym)
        {
            return sym.ContainingAssembly != m_AssemblySymbol;
        }
        internal bool IsExternSymbol(IFieldSymbol sym)
        {
            return sym.ContainingAssembly != m_AssemblySymbol;
        }
        internal bool IsExternSymbol(IPropertySymbol sym)
        {
            return sym.ContainingAssembly != m_AssemblySymbol;
        }
        internal bool IsExternSymbol(IEventSymbol sym)
        {
            return sym.ContainingAssembly != m_AssemblySymbol;
        }
        internal bool IsExternSymbol(ITypeSymbol sym)
        {
            return sym.ContainingAssembly != m_AssemblySymbol;
        }

        internal bool IsLegalGenericType(INamedTypeSymbol sym)
        {
            return IsLegalGenericType(sym, false);
        }
        internal bool IsLegalGenericType(INamedTypeSymbol sym, bool isAccessMember)
        {
            var name = CalcFullNameWithTypeParameters(sym, true);
            bool ret = m_LegalGenericTypes.Contains(name);
            if (!ret && sym.IsGenericType) {                
                name = CalcFullNameAndTypeArguments(name, sym);
                ret = m_LegalGenericTypes.Contains(name);
            }
            if (!ret) {
                if (isAccessMember) {
                    if (!m_AccessMemberOfIllegalGenericTypes.Contains(name)) {
                        m_AccessMemberOfIllegalGenericTypes.Add(name);
                    }
                } else {
                    if (!m_IllegalGenericTypes.Contains(name)) {
                        m_IllegalGenericTypes.Add(name);
                    }
                }
            }
            return ret;
        }
        internal bool IsLegalGenericMethod(IMethodSymbol sym)
        {
            var type = CalcFullNameWithTypeParameters(sym.ContainingType, true);
            var name = sym.Name;
            var fullName = string.Format("{0}.{1}", type, name);
            bool ret = m_LegalGenericMethods.Contains(fullName);
            if (!ret) {
                if (!m_IllegalGenericMethods.Contains(fullName)) {
                    m_IllegalGenericMethods.Add(fullName);
                }
            }
            return ret;
        }
        internal bool IsLegalParameterGenericType(INamedTypeSymbol sym)
        {
            var name = CalcFullNameWithTypeParameters(sym, true);
            bool ret = m_LegalParameterGenericTypes.Contains(name);
            if (!ret && sym.IsGenericType) {
                name = CalcFullNameAndTypeArguments(name, sym);
                ret = m_LegalParameterGenericTypes.Contains(name);
            }
            if (!ret) {
                if (!m_IllegalParameterGenericTypes.Contains(name)) {
                    m_IllegalParameterGenericTypes.Add(name);
                }
            }
            return ret;
        }
        internal bool IsLegalExtension(INamedTypeSymbol sym)
        {
            var name = CalcFullNameWithTypeParameters(sym, true);
            bool ret = m_LegalExtensions.Contains(name);
            if (!ret) {
                if (!m_IllegalExtensions.Contains(name)) {
                    m_IllegalExtensions.Add(name);
                }
            }
            return ret;
        }
        internal bool IsLegalConvertion(INamedTypeSymbol srcSym, INamedTypeSymbol targetSym)
        {
            var srcName = CalcFullNameWithTypeParameters(srcSym, true);
            var targetName = CalcFullNameWithTypeParameters(targetSym, true);
            bool ret = false;
            HashSet<string> targets;
            if(m_LegalConvertions.TryGetValue(srcName, out targets)) {
                ret = targets.Contains(targetName);
            }
            string newTargetName = null;
            if (!ret && null != targets && targetSym.IsGenericType) {
                newTargetName = CalcFullNameAndTypeArguments(targetName, targetSym);
                ret = targets.Contains(newTargetName);
            }
            string newSrcName = null;
            if (!ret && srcSym.IsGenericType) {
                newSrcName = CalcFullNameAndTypeArguments(srcName, srcSym);
                if (m_LegalConvertions.TryGetValue(newSrcName, out targets)) {
                    ret = targets.Contains(targetName);
                    if(!ret && targetSym.IsGenericType) {
                        if (null == newTargetName)
                            newTargetName = CalcFullNameAndTypeArguments(targetName, targetSym);
                        ret = targets.Contains(newTargetName);
                    }
                }
            }
            if (!ret) {
                var src = srcName;
                if (null != newSrcName) {
                    src = newSrcName;
                }
                var target = targetName;
                if (null != newTargetName) {
                    target = newTargetName;
                }
                HashSet<string> illegalTargets;
                if(!m_IllegalConvertions.TryGetValue(src, out illegalTargets)) {
                    illegalTargets = new HashSet<string>();
                    m_IllegalConvertions.Add(src, illegalTargets);
                }
                if (!illegalTargets.Contains(target)) {
                    illegalTargets.Add(target);
                }
            }
            return ret;
        }
        internal bool IsIllegalType(INamedTypeSymbol sym)
        {
            var name = CalcFullNameWithTypeParameters(sym, true);
            var ret = m_IllegalTypes.Contains(name);
            if (!ret && sym.IsGenericType) {
                var str = CalcFullNameAndTypeArguments(name, sym);
                ret = m_IllegalTypes.Contains(str);
            }
            return ret;
        }
        internal bool IsIllegalMethod(IMethodSymbol sym)
        {
            var type = CalcFullNameWithTypeParameters(sym.ContainingType, true);
            var name = sym.Name;
            var fullName = string.Format("{0}.{1}", type, name);
            bool ret = m_IllegalMethods.Contains(fullName);
            return ret;
        }
        internal bool IsIllegalProperty(IPropertySymbol sym)
        {
            var type = CalcFullNameWithTypeParameters(sym.ContainingType, true);
            var name = sym.Name;
            var fullName = string.Format("{0}.{1}", type, name);
            bool ret = m_IllegalProperties.Contains(fullName);
            return ret;
        }
        internal bool IsIllegalField(IFieldSymbol sym)
        {
            var type = CalcFullNameWithTypeParameters(sym.ContainingType, true);
            var name = sym.Name;
            var fullName = string.Format("{0}.{1}", type, name);
            bool ret = m_IllegalFields.Contains(fullName);
            return ret;
        }

        private void InitRecursively(string rootNs, INamespaceSymbol nssym)
        {
            if (null != nssym) {
                string ns = GetNamespaces(nssym);
                if (string.IsNullOrEmpty(ns)) {
                    foreach (var newSym in nssym.GetNamespaceMembers()) {
                        ns = GetNamespaces(newSym);
                        if (ns == rootNs) {
                            InitRecursively(newSym);
                            break;
                        }
                    }
                } else if (ns == rootNs) {
                    InitRecursively(nssym);
                }
            }
        }
        private void InitRecursively(INamespaceSymbol nssym)
        {
            string ns = GetNamespaces(nssym);
            m_Namespaces.Add(ns);
            foreach (var newSym in nssym.GetNamespaceMembers()) {
                InitRecursively(newSym);
            }
        }
        
        private SymbolTable() { }

        private CSharpCompilation m_Compilation = null;
        private IAssemblySymbol m_AssemblySymbol = null;
        private HashSet<string> m_Namespaces = new HashSet<string>();
        private HashSet<string> m_LegalGenericTypes = new HashSet<string>();
        private HashSet<string> m_LegalGenericMethods = new HashSet<string>();
        private HashSet<string> m_LegalParameterGenericTypes = new HashSet<string>();
        private HashSet<string> m_LegalExtensions = new HashSet<string>();
        private Dictionary<string, HashSet<string>> m_LegalConvertions = new Dictionary<string, HashSet<string>>();

        private HashSet<string> m_IllegalTypes = new HashSet<string>();
        private HashSet<string> m_IllegalMethods = new HashSet<string>();
        private HashSet<string> m_IllegalProperties = new HashSet<string>();
        private HashSet<string> m_IllegalFields = new HashSet<string>();

        private HashSet<string> m_IllegalGenericTypes = new HashSet<string>();
        private HashSet<string> m_IllegalGenericMethods = new HashSet<string>();
        private HashSet<string> m_IllegalParameterGenericTypes = new HashSet<string>();
        private HashSet<string> m_IllegalExtensions = new HashSet<string>();
        private Dictionary<string, HashSet<string>> m_IllegalConvertions = new Dictionary<string, HashSet<string>>();

        private HashSet<string> m_AccessMemberOfIllegalGenericTypes = new HashSet<string>();
        
        internal static SymbolTable Instance
        {
            get { return s_Instance; }
        }
        private static SymbolTable s_Instance = new SymbolTable();
        
        internal static bool IsImplementationOfSys(ITypeSymbol symInfo, string name)
        {
            if (null != symInfo) {
                foreach (var intf in symInfo.AllInterfaces) {
                    if (intf.Name == name) {
                        string ns = GetNamespaces(intf.ContainingNamespace);
                        if (ns.StartsWith("System.") || ns == "System") {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        internal static string CalcFullNameWithTypeParameters(ISymbol type, bool includeSelfName)
        {
            if (null == type)
                return string.Empty;
            List<string> list = new List<string>();
            if (includeSelfName) {
                list.Add(CalcNameWithTypeParameters(type));
            }
            INamespaceSymbol ns = type.ContainingNamespace;
            var ct = type.ContainingType;
            string name = string.Empty;
            if (null != ct) {
                name = CalcNameWithTypeParameters(ct);
            }
            while (null != ct && name.Length > 0) {
                list.Insert(0, name);
                ns = ct.ContainingNamespace;
                ct = ct.ContainingType;
                if (null != ct) {
                    name = CalcNameWithTypeParameters(ct);
                } else {
                    name = string.Empty;
                }
            }
            while (null != ns && ns.Name.Length > 0) {
                list.Insert(0, ns.Name);
                ns = ns.ContainingNamespace;
            }
            return string.Join(".", list.ToArray());
        }
        internal static string CalcNameWithTypeParameters(ISymbol sym)
        {
            if (null == sym)
                return string.Empty;
            var typeSym = sym as INamedTypeSymbol;
            if (null != typeSym) {
                return CalcNameWithTypeParameters(typeSym);
            } else {
                return sym.Name;
            }
        }
        internal static string CalcNameWithTypeParameters(INamedTypeSymbol type)
        {
            if (null == type)
                return string.Empty;
            List<string> list = new List<string>();
            list.Add(type.Name);
            foreach (var param in type.TypeParameters) {
                list.Add(param.Name);
            }
            return string.Join("_", list.ToArray());
        }
        private static string CalcFullNameAndTypeArguments(string name, INamedTypeSymbol sym)
        {
            if (sym.IsGenericType) {
                StringBuilder sb = new StringBuilder();
                sb.Append(name);
                sb.Append('|');
                string prestr = string.Empty;
                foreach (var ta in sym.TypeArguments) {
                    sb.Append(prestr);
                    if (ta.TypeKind == TypeKind.Delegate) {
                        sb.AppendFormat("\"{0}\"", CalcFullNameWithTypeParameters(ta, true));
                    }
                    else if (ta.TypeKind == TypeKind.TypeParameter) {
                        sb.Append(ta.Name);
                    }
                    else {
                        sb.Append(CalcFullNameWithTypeParameters(ta, true));
                    }
                    prestr = ", ";
                }
                return sb.ToString();
            }
            else {
                return name;
            }
        }
        private static string GetNamespaces(INamespaceSymbol ns)
        {
            List<string> list = new List<string>();
            while (null != ns && ns.Name.Length > 0) {
                list.Insert(0, ns.Name);
                ns = ns.ContainingNamespace;
            }
            return string.Join(".", list.ToArray());
        }
    }
}