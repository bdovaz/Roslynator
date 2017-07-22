﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Roslynator.CSharp.CSharpFactory;

namespace Roslynator.CSharp.CodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveImplementationFromAbstractMemberCodeFixProvider))]
    [Shared]
    public class RemoveImplementationFromAbstractMemberCodeFixProvider : BaseCodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(
                    CompilerDiagnosticIdentifiers.EventInInterfaceCannotHaveAddOrRemoveAccessors,
                    CompilerDiagnosticIdentifiers.MemberCannotDeclareBodyBecauseItIsMarkedAbstract,
                    CompilerDiagnosticIdentifiers.InterfaceMembersCannotHaveDefinition);
            }
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            if (!Settings.IsCodeFixEnabled(CodeFixIdentifiers.RemoveImplementationFromAbstractMember))
                return;

            SyntaxNode root = await context.GetSyntaxRootAsync().ConfigureAwait(false);

            SyntaxNode node = root
                .FindNode(context.Span, getInnermostNodeForTie: true)?
                .FirstAncestorOrSelf(f => f is MemberDeclarationSyntax || f is AccessorDeclarationSyntax);

            Debug.Assert(node != null, $"{nameof(node)} is null");

            if (node == null)
                return;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                switch (diagnostic.Id)
                {
                    case CompilerDiagnosticIdentifiers.EventInInterfaceCannotHaveAddOrRemoveAccessors:
                    case CompilerDiagnosticIdentifiers.MemberCannotDeclareBodyBecauseItIsMarkedAbstract:
                    case CompilerDiagnosticIdentifiers.InterfaceMembersCannotHaveDefinition:
                        {
                            if (node.IsKind(SyntaxKind.AddAccessorDeclaration, SyntaxKind.RemoveAccessorDeclaration))
                                node = node.Parent.Parent;

                            CodeAction codeAction = CodeAction.Create(
                                (node.IsKind(SyntaxKind.EventDeclaration)) ? "Remove accessor" : "Remove body",
                                cancellationToken => RefactorAsync(context.Document, node, cancellationToken),
                                GetEquivalenceKey(diagnostic));

                            context.RegisterCodeFix(codeAction, diagnostic);
                            break;
                        }
                }
            }
        }

        private static Task<Document> RefactorAsync(
            Document document,
            SyntaxNode node,
            CancellationToken cancellationToken)
        {
            switch (node.Kind())
            {
                case SyntaxKind.MethodDeclaration:
                    {
                        var methodDeclaration = (MethodDeclarationSyntax)node;

                        MethodDeclarationSyntax newNode = methodDeclaration
                            .WithBody(null)
                            .WithExpressionBody(null)
                            .WithSemicolonToken(SemicolonToken())
                            .WithFormatterAnnotation();

                        return document.ReplaceNodeAsync(methodDeclaration, newNode, cancellationToken);
                    }
                case SyntaxKind.PropertyDeclaration:
                    {
                        var propertyDeclaration = (PropertyDeclarationSyntax)node;
                        ArrowExpressionClauseSyntax expressionBody = propertyDeclaration.ExpressionBody;

                        PropertyDeclarationSyntax newNode = propertyDeclaration
                            .WithExpressionBody(null)
                            .WithoutSemicolonToken()
                            .WithAccessorList(AccessorList(AutoGetAccessorDeclaration()).WithTriviaFrom(expressionBody))
                            .WithFormatterAnnotation();

                        return document.ReplaceNodeAsync(propertyDeclaration, newNode, cancellationToken);
                    }
                case SyntaxKind.IndexerDeclaration:
                    {
                        var indexerDeclaration = (IndexerDeclarationSyntax)node;
                        ArrowExpressionClauseSyntax expressionBody = indexerDeclaration.ExpressionBody;

                        IndexerDeclarationSyntax newNode = indexerDeclaration
                            .WithExpressionBody(null)
                            .WithoutSemicolonToken()
                            .WithAccessorList(AccessorList(AutoGetAccessorDeclaration()).WithTriviaFrom(expressionBody))
                            .WithFormatterAnnotation();

                        return document.ReplaceNodeAsync(indexerDeclaration, newNode, cancellationToken);
                    }
                case SyntaxKind.EventDeclaration:
                    {
                        var eventDeclaration = (EventDeclarationSyntax)node;

                        EventFieldDeclarationSyntax eventFieldDeclaration = EventFieldDeclaration(
                            eventDeclaration.AttributeLists,
                            eventDeclaration.Modifiers,
                            eventDeclaration.EventKeyword,
                            VariableDeclaration(eventDeclaration.Type, VariableDeclarator(eventDeclaration.Identifier)),
                            SemicolonToken());

                        eventFieldDeclaration = eventFieldDeclaration.WithFormatterAnnotation();

                        return document.ReplaceNodeAsync(eventDeclaration, eventFieldDeclaration, cancellationToken);
                    }
                case SyntaxKind.GetAccessorDeclaration:
                case SyntaxKind.SetAccessorDeclaration:
                    {
                        var accessor = (AccessorDeclarationSyntax)node;

                        AccessorDeclarationSyntax newAccessor = accessor
                            .WithBody(null)
                            .WithExpressionBody(null)
                            .WithSemicolonToken(SemicolonToken())
                            .WithTrailingTrivia(
                                accessor.DescendantTrivia(
                                    TextSpan.FromBounds(
                                        accessor.BodyOrExpressionBody().SpanStart,
                                        accessor.Span.End)))
                            .WithFormatterAnnotation();

                        return document.ReplaceNodeAsync(accessor, newAccessor, cancellationToken);
                    }
            }

            Debug.Fail("");

            return Task.FromResult(document);
        }
    }
}