using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Semantics;
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
            var esym = m_Model.GetSymbolInfo(node).Symbol as IEventSymbol;
            var psym = m_Model.GetSymbolInfo(node).Symbol as IPropertySymbol;
            var fsym = m_Model.GetSymbolInfo(node).Symbol as IFieldSymbol;
            var msym = m_Model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            var oper = m_Model.GetOperation(node);
            
            SyntaxNode newNode = null;
            var name = node.Expression as IdentifierNameSyntax;
            if (null != name) {
                var text = name.Identifier.Text;
                foreach (var ns in SymbolTable.Instance.Namespaces) {
                    if (ns == text || ns.Contains("." + text + ".") || ns.EndsWith("." + text)) {
                        newNode = SyntaxFactory.IdentifierName(node.Name.ToString()).WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia());
                    }
                }
            }
            if (null == newNode) {
                newNode = base.VisitMemberAccessExpression(node);
            }
            bool isExtern = false;
            INamedTypeSymbol classType = null;
            if (null != esym && SymbolTable.Instance.IsExternSymbol(esym)) {
                isExtern = true;
                classType = esym.ContainingType;
                newNode = ReportAndAttachError(newNode, string.Format("[Cs2LuaRewriter] Unsupported extern event !"));
            }
            if (null != psym && SymbolTable.Instance.IsExternSymbol(psym)) {
                isExtern = true;
                classType = psym.ContainingType;
                if (SymbolTable.Instance.IsIllegalProperty(psym)) {
                    newNode = ReportAndAttachError(newNode, string.Format("[Cs2LuaRewriter] Unsupported extern property !"));
                }
            }
            if (null != fsym && SymbolTable.Instance.IsExternSymbol(fsym)) {
                isExtern = true;
                classType = fsym.ContainingType;
                if (SymbolTable.Instance.IsIllegalField(fsym)) {
                    newNode = ReportAndAttachError(newNode, string.Format("[Cs2LuaRewriter] Unsupported extern field !"));
                }
            }
            if (null != msym && !(node.Parent is InvocationExpressionSyntax) && SymbolTable.Instance.IsExternSymbol(msym.ContainingType)) {
                if (SymbolTable.Instance.IsIllegalMethod(msym)) {
                    newNode = ReportAndAttachError(newNode, "[Cs2LuaRewriter] Unsupported extern method !");
                }
            }
            if (isExtern && null != oper) {
                bool legal = true;
                SymbolTable.TryRemoveNullable(ref classType);
                if (null != classType && (classType.TypeKind == TypeKind.Delegate || classType.IsGenericType && SymbolTable.Instance.IsLegalGenericType(classType, true))) {
                    //如果是标记为合法的泛型类或委托类型的成员，则不用再进行类型检查
                } else {
                    var type = oper.Type as INamedTypeSymbol;
                    SymbolTable.TryRemoveNullable(ref type);
                    if (null != type && SymbolTable.Instance.IsExternSymbol(type) && type.TypeKind != TypeKind.Delegate) {
                        if (type.IsGenericType) {
                            if (!SymbolTable.Instance.IsLegalParameterGenericType(type)) {
                                legal = false;
                            }
                        } else {
                            if (SymbolTable.Instance.IsIllegalType(type)) {
                                legal = false;
                            }
                        }
                    }
                }
                if (!legal) {
                    newNode = ReportAndAttachError(newNode, string.Format("[Cs2LuaRewriter] Unsupported extern type from member access !"));
                }
            }
            return newNode;
        }
        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var newNode = base.VisitVariableDeclarator(node);
            if (null != node.Initializer) {
                var declSym = m_Model.GetDeclaredSymbol(node) as ILocalSymbol;
                var init = node.Initializer;
                var srcOper = m_Model.GetOperation(init.Value);
                if (null != srcOper && !CheckConvert(srcOper.Type, declSym.Type)) {
                    newNode = ReportAndAttachError(node, "[Cs2LuaRewriter] Can't convert extern type to generic type or target type is unsupported !");
                }
            }
            return newNode;
        }
        public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            var newNode = base.VisitAssignmentExpression(node);
            var leftOper = m_Model.GetOperation(node.Left);
            var rightOper = m_Model.GetOperation(node.Right);
            if (null != leftOper && null != rightOper && !CheckConvert(rightOper.Type, leftOper.Type)) {
                newNode = ReportAndAttachError(node, "[Cs2LuaRewriter] Can't convert extern type to generic type or target type is unsupported !");
            }
            return newNode;
        }
        public override SyntaxNode VisitCastExpression(CastExpressionSyntax node)
        {
            var newNode = base.VisitCastExpression(node);
            var oper = m_Model.GetOperation(node) as IConversionExpression;
            var opd = oper.Operand as IConversionExpression;
            if (null != oper && oper.UsesOperatorMethod) {
            }
            else {
                var typeInfo = m_Model.GetTypeInfo(node.Type);
                var type = typeInfo.Type;
                var srcOper = m_Model.GetOperation(node.Expression);
                if (null != srcOper && !CheckConvert(srcOper.Type, type)) {
                    newNode = ReportAndAttachError(node, "[Cs2LuaRewriter] Can't convert extern type to generic type or target type is unsupported !");
                }
            }
            return newNode;
        }
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var sym = m_Model.GetSymbolInfo(node).Symbol as IMethodSymbol;

            var newNode = base.VisitInvocationExpression(node);
            if (null != sym && SymbolTable.Instance.IsExternSymbol(sym)) {
                if (sym.IsExtensionMethod && !SymbolTable.Instance.IsLegalExtension(sym.ContainingType)) {
                    //不支持的语法
                    newNode = ReportAndAttachError(newNode, "[Cs2LuaRewriter] Unsupported extension method !");
                } else {
                    if (SymbolTable.Instance.IsIllegalMethod(sym)) {
                        newNode = ReportAndAttachError(newNode, "[Cs2LuaRewriter] Unsupported extern method !");
                    }
                }
                bool legal = true;
                if (sym.ContainingType.TypeKind == TypeKind.Delegate || sym.ContainingType.IsGenericType && SymbolTable.Instance.IsLegalGenericType(sym.ContainingType, true) || sym.IsGenericMethod && SymbolTable.Instance.IsLegalGenericMethod(sym)) {
                    //如果是标记为合法的泛型类或委托类型的成员，则不用再进行类型检查
                } else {
                    foreach (var param in sym.Parameters) {
                        var type = param.Type as INamedTypeSymbol;
                        if (null != type && type.TypeKind != TypeKind.Delegate) {
                            if (type.IsGenericType) {
                                if (!SymbolTable.Instance.IsLegalParameterGenericType(type)) {
                                    legal = false;
                                }
                            } else {
                                if (SymbolTable.Instance.IsIllegalType(type)) {
                                    legal = false;
                                }
                            }
                        }
                    }
                    if (!sym.ReturnsVoid) {
                        var type = sym.ReturnType as INamedTypeSymbol;
                        if (null != type && type.TypeKind != TypeKind.Delegate) {
                            if (type.IsGenericType) {
                                if (!SymbolTable.Instance.IsLegalParameterGenericType(type)) {
                                    legal = false;
                                }
                            } else {
                                if (SymbolTable.Instance.IsIllegalType(type)) {
                                    legal = false;
                                }
                            }
                        }
                    }
                }
                if (!legal) {
                    newNode = ReportAndAttachError(newNode, string.Format("[Cs2LuaRewriter] Unsupported extern type from invocation's return or out param !"));
                }
            }
            return newNode;
        }
        public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            bool legal = true;
            var oper = m_Model.GetOperation(node);
            if (null != oper) {
                var type = oper.Type as INamedTypeSymbol;
                SymbolTable.TryRemoveNullable(ref type);
                if (null != type && SymbolTable.Instance.IsExternSymbol(type)) {
                    if (type.IsGenericType) {
                        if (!SymbolTable.Instance.IsLegalGenericType(type)) {
                            legal = false;
                        }
                    } else {
                        if (SymbolTable.Instance.IsIllegalType(type)) {
                            legal = false;
                        }
                    }
                }
            }
            var newNode = base.VisitObjectCreationExpression(node);
            if (!legal) {
                newNode = ReportAndAttachError(newNode, "[Cs2LuaRewriter] Unsupported extern type !");
            }
            return newNode;
        }
        public override SyntaxNode VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            bool legal = true;
            var oper = m_Model.GetOperation(node) as ITypeOfExpression;
            var type = oper.TypeOperand as INamedTypeSymbol;
            SymbolTable.TryRemoveNullable(ref type);
            if (null != type && SymbolTable.Instance.IsExternSymbol(type)) {
                if (type.IsGenericType) {
                    if (!SymbolTable.Instance.IsLegalGenericType(type)) {
                        legal = false;
                    }
                }
                else {
                    if (SymbolTable.Instance.IsIllegalType(type)) {
                        legal = false;
                    }
                }
            }
            var newNode = base.VisitTypeOfExpression(node);
            if (!legal) {
                newNode = ReportAndAttachError(newNode, "[Cs2LuaRewriter] Unsupported extern type !");
            }
            return newNode;
        }
        public override SyntaxNode VisitJoinClause(JoinClauseSyntax node)
        {
            //不支持的语法
            return ReportAndAttachError(base.VisitJoinClause(node), "[Cs2LuaRewriter] Unsupported linq Syntax !");
        }
        public override SyntaxNode VisitJoinIntoClause(JoinIntoClauseSyntax node)
        {
            //不支持的语法
            return ReportAndAttachError(base.VisitJoinIntoClause(node), "[Cs2LuaRewriter] Unsupported linq Syntax !");
        }
        public override SyntaxNode VisitGroupClause(GroupClauseSyntax node)
        {
            //不支持的语法
            return ReportAndAttachError(base.VisitGroupClause(node), "[Cs2LuaRewriter] Unsupported linq Syntax !");
        }
        public override SyntaxNode VisitQueryContinuation(QueryContinuationSyntax node)
        {
            //不支持的语法
            return ReportAndAttachError(base.VisitQueryContinuation(node), "[Cs2LuaRewriter] Unsupported linq Syntax !");
        }
        public override SyntaxNode VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
        {
            //不支持的语法
            return ReportAndAttachError(base.VisitCasePatternSwitchLabel(node), "[Cs2LuaRewriter] Unsupported 'case pattern' Syntax !");
        }
        public override SyntaxNode VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            //不支持的语法
            return ReportAndAttachError(base.VisitIsPatternExpression(node), "[Cs2LuaRewriter] Unsupported 'is pattern' Syntax !");
        }
        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            //不支持的语法
            return ReportAndAttachError(base.VisitLocalFunctionStatement(node), "[Cs2LuaRewriter] Unsupported 'local function' Syntax !");
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
        private bool CheckConvert(ITypeSymbol srcTypeSym, ITypeSymbol targetTypeSym)
        {
            var srcNamedTypeSym = srcTypeSym as INamedTypeSymbol;
            var targetNamedTypeSym = targetTypeSym as INamedTypeSymbol;
            SymbolTable.TryRemoveNullable(ref srcNamedTypeSym);
            SymbolTable.TryRemoveNullable(ref targetNamedTypeSym);
            if (null != srcNamedTypeSym && null != targetNamedTypeSym){
                if (null != srcNamedTypeSym.ContainingType && SymbolTable.CalcFullNameWithTypeParameters(srcNamedTypeSym, true) == "System.Object" && SymbolTable.Instance.IsExternSymbol(srcNamedTypeSym.ContainingType) && targetNamedTypeSym.TypeKind != TypeKind.Delegate && (targetNamedTypeSym.IsGenericType || !SymbolTable.Instance.IsExternSymbol(targetNamedTypeSym))) {
                    if (!SymbolTable.Instance.IsLegalConvertion(srcNamedTypeSym, targetNamedTypeSym)) {
                        return false;
                    }
                }
                else if (null != targetNamedTypeSym.ContainingType && SymbolTable.CalcFullNameWithTypeParameters(targetNamedTypeSym, true) == "System.Object" && SymbolTable.Instance.IsExternSymbol(targetNamedTypeSym.ContainingType) && srcNamedTypeSym.TypeKind != TypeKind.Delegate && (srcNamedTypeSym.IsGenericType || !SymbolTable.Instance.IsExternSymbol(srcNamedTypeSym))) {
                    if (!SymbolTable.Instance.IsLegalConvertion(srcNamedTypeSym, targetNamedTypeSym)) {
                        return false;
                    }
                }
                else if (srcNamedTypeSym.TypeKind == TypeKind.Delegate || targetNamedTypeSym.TypeKind == TypeKind.Delegate) {
                    //delegate之间赋值认为合法
                }
                else if (!srcNamedTypeSym.IsGenericType && SymbolTable.Instance.IsExternSymbol(srcNamedTypeSym) && targetNamedTypeSym.IsGenericType) {
                    if (!SymbolTable.Instance.IsLegalConvertion(srcNamedTypeSym, targetNamedTypeSym)) {
                        return false;
                    }
                }
            }
            if (null != targetNamedTypeSym && SymbolTable.Instance.IsExternSymbol(targetNamedTypeSym) && SymbolTable.Instance.IsIllegalType(targetNamedTypeSym)) {
                return false;
            }
            return true;
        }
        private SyntaxNode ReportAndAttachError(SyntaxNode node, string errInfo)
        {
            Logger.Instance.Log(node, errInfo);
            return node.WithLeadingTrivia(SyntaxFactory.Comment(string.Format("/* {0} */", errInfo)));
        }

        public Cs2LuaRewriter(string rootNs, SemanticModel model)
        {
            m_RootNs = rootNs;
            m_Model = model;
        }

        private string m_RootNs = string.Empty;
        private SemanticModel m_Model = null;
    }
}
