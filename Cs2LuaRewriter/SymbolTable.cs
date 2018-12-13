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
        internal void Init(CSharpCompilation compilation, string rootNs)
        {
            m_Compilation = compilation;
            m_AssemblySymbol = compilation.Assembly;
            INamespaceSymbol nssym = m_AssemblySymbol.GlobalNamespace;
            InitRecursively(rootNs, nssym);
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

        private string GetNamespaces(INamespaceSymbol ns)
        {
            List<string> list = new List<string>();
            while (null != ns && ns.Name.Length > 0) {
                list.Insert(0, ns.Name);
                ns = ns.ContainingNamespace;
            }
            return string.Join(".", list.ToArray());
        }

        private SymbolTable() { }

        private CSharpCompilation m_Compilation = null;
        private IAssemblySymbol m_AssemblySymbol = null;
        private HashSet<string> m_Namespaces = new HashSet<string>();

        internal static SymbolTable Instance
        {
            get { return s_Instance; }
        }
        private static SymbolTable s_Instance = new SymbolTable();
    }
}
