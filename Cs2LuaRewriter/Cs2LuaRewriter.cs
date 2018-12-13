using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace RoslynTool
{
    internal class Cs2LuaRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            /*
            //测试语法规则用
            if (node.Identifier.Text == m_RootNs) {
                if (node.Parent is UsingDirectiveSyntax || node.Parent is NamespaceDeclarationSyntax) {
                } else {
                    var ma = node.Parent as MemberAccessExpressionSyntax;
                    if (null != ma) {
                    } else {
                    }
                }
            }
            */
            return base.VisitIdentifierName(node);
        }
        public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        {
            var fullName = node.ToString();
            if (SymbolTable.Instance.Namespaces.Contains(fullName)) {
                return node;
            }
            var leftFullName = node.Left.ToString();
            if (SymbolTable.Instance.Namespaces.Contains(leftFullName)) {
                return node.Right;
            }
            foreach (var ns in SymbolTable.Instance.Namespaces) {
                if (leftFullName.IndexOf(ns) == 0) {
                    var leftName = leftFullName.Substring(ns.Length + 1);
                    return SyntaxFactory.QualifiedName(SyntaxFactory.GenericName(leftName), node.Right);
                }
            }
            return base.VisitQualifiedName(node);
        }
        public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var name = node.Expression as IdentifierNameSyntax;
            if (null != name) {
                var text = name.Identifier.Text;
                foreach (var ns in SymbolTable.Instance.Namespaces) {
                    if (ns == text || ns.Contains("." + text + ".") || ns.EndsWith("." + text)) {
                        return SyntaxFactory.IdentifierName(node.Name.ToString()).WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia());
                    }
                }
            }
            return base.VisitMemberAccessExpression(node);
        }
        public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
        {
            node = base.VisitCompilationUnit(node) as CompilationUnitSyntax;

            List<ExternAliasDirectiveSyntax> addExterns = new List<ExternAliasDirectiveSyntax>();
            List<UsingDirectiveSyntax> addUsings = new List<UsingDirectiveSyntax>();
            List<MemberDeclarationSyntax> addMembers = new List<MemberDeclarationSyntax>();
            addExterns.AddRange(node.Externs);
            foreach (var us in node.Usings) {
                if (us.Name.ToString().IndexOf(m_RootNs) == 0) {
                } else {
                    addUsings.Add(us);
                }
            }
            foreach (var member in node.Members) {
                var ns = member as NamespaceDeclarationSyntax;
                if (null != ns) {
                    if (ns.Name.ToString().IndexOf(m_RootNs) == 0) {
                        ExtractMembersRecursively(addExterns, addUsings, addMembers, ns);
                    } else {
                        addMembers.Add(member);
                    }
                } else {
                    addMembers.Add(member);
                }
            }
            var newNode = SyntaxFactory.CompilationUnit(SyntaxFactory.List<ExternAliasDirectiveSyntax>(addExterns), SyntaxFactory.List<UsingDirectiveSyntax>(addUsings), node.AttributeLists, SyntaxFactory.List<MemberDeclarationSyntax>(addMembers));
            return newNode.WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia()).WithEndOfFileToken(node.EndOfFileToken);
        }

        private void ExtractMembersRecursively(List<ExternAliasDirectiveSyntax> addExterns, List<UsingDirectiveSyntax> addUsings, List<MemberDeclarationSyntax> addMembers, NamespaceDeclarationSyntax ns)
        {
            addExterns.AddRange(ns.Externs);
            foreach (var us in ns.Usings) {
                if (us.Name.ToString().IndexOf(m_RootNs) != 0) {
                    addUsings.Add(us);
                }
            }
            foreach (var member in ns.Members) {
                var cns = member as NamespaceDeclarationSyntax;
                if (null == cns) {
                    addMembers.Add(member);
                } else {
                    ExtractMembersRecursively(addExterns, addUsings, addMembers, cns);
                }
            }
        }

        public Cs2LuaRewriter(string rootNs)
        {
            m_RootNs = rootNs;
        }

        private string m_RootNs = string.Empty;
    }
}
