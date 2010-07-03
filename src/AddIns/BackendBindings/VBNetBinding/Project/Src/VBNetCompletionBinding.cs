// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Markus Palme" email="MarkusPalme@gmx.de"/>
//     <version>$Revision$</version>
// </file>

using ICSharpCode.SharpDevelop.Editor.CodeCompletion;
using System;
using ICSharpCode.Core;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.Parser;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Dom.Refactoring;
using ICSharpCode.SharpDevelop.Dom.VBNet;
using ICSharpCode.SharpDevelop.Editor;
using VBTokens = ICSharpCode.NRefactory.Parser.VB.Tokens;

namespace VBNetBinding
{
	public class VBNetCompletionBinding : NRefactoryCodeCompletionBinding
	{
		public VBNetCompletionBinding()
			: base(SupportedLanguage.VBNet)
		{
			// Don't use indexer insight for '[', VB uses '(' for indexer access
			this.EnableIndexerInsight = false;
		}
		
		public override CodeCompletionKeyPressResult HandleKeyPress(ITextEditor editor, char ch)
		{
			if(ch == '(' && EnableMethodInsight && CodeCompletionOptions.InsightEnabled) {
				IInsightWindow insightWindow = editor.ShowInsightWindow(new MethodInsightProvider().ProvideInsight(editor));
				if (insightWindow != null)
					InitializeOpenedInsightWindow(editor, insightWindow);
				return CodeCompletionKeyPressResult.Completed;
			} else if(ch == ',' && CodeCompletionOptions.InsightRefreshOnComma && CodeCompletionOptions.InsightEnabled) {
				if (InsightRefreshOnComma(editor, ch))
					return CodeCompletionKeyPressResult.Completed;
			} else if (ch == '\n') {
				TryDeclarationTypeInference(editor, editor.Document.GetLineForOffset(editor.Caret.Offset));
			}
			return base.HandleKeyPress(editor, ch);
		}
		
		bool IsInComment(ITextEditor editor)
		{
			VBExpressionFinder ef = new VBExpressionFinder();
			int cursor = editor.Caret.Offset - 1;
			return ef.FilterComments(editor.Document.GetText(0, cursor + 1), ref cursor) == null;
		}
		
		sealed class GlobalCompletionItemProvider : CodeCompletionItemProvider
		{
			public override ExpressionResult GetExpression(ITextEditor editor)
			{
				return new ExpressionResult("Global", ExpressionContext.Importable);
			}
		}
		
		public override bool HandleKeyword(ITextEditor editor, string word)
		{
			// TODO: Assistance writing Methods/Fields/Properties/Events:
			// use public/static/etc. as keywords to display a list with other modifiers
			// and possible return types.
			switch (word.ToLowerInvariant()) {
				case "imports":
					if (IsInComment(editor)) return false;
					new GlobalCompletionItemProvider().ShowCompletion(editor);
					return true;
				case "as":
					if (IsInComment(editor)) return false;
					CtrlSpaceCompletionItemProvider(ExpressionContext.Type).ShowCompletion(editor);
					return true;
				case "new":
					if (IsInComment(editor)) return false;
					CtrlSpaceCompletionItemProvider(ExpressionContext.ObjectCreation).ShowCompletion(editor);
					return true;
				case "inherits":
					if (IsInComment(editor)) return false;
					CtrlSpaceCompletionItemProvider(ExpressionContext.Type).ShowCompletion(editor);
					return true;
				case "implements":
					if (IsInComment(editor)) return false;
					CtrlSpaceCompletionItemProvider(ExpressionContext.Interface).ShowCompletion(editor);
					return true;
				case "overrides":
					if (IsInComment(editor)) return false;
					new OverrideCompletionItemProvider().ShowCompletion(editor);
					return true;
				case "return":
					if (IsInComment(editor)) return false;
					IMember m = GetCurrentMember(editor);
					if (m != null) {
						ProvideContextCompletion(editor, m.ReturnType, ' ');
						return true;
					} else {
						goto default;
					}
				case "option":
					if (IsInComment(editor)) return false;
					new TextCompletionItemProvider("Explicit On",
					                               "Explicit Off",
					                               "Strict On",
					                               "Strict Off",
					                               "Compare Binary",
					                               "Compare Text",
					                               "Infer On",
					                               "Infer Off")
						.ShowCompletion(editor);
					return true;
				default:
					return base.HandleKeyword(editor, word);
			}
		}
		
		CtrlSpaceCompletionItemProvider CtrlSpaceCompletionItemProvider(ExpressionContext context)
		{
			return new NRefactoryCtrlSpaceCompletionItemProvider(LanguageProperties.VBNet, context);
		}
		
		bool TryDeclarationTypeInference(ITextEditor editor, IDocumentLine curLine)
		{
			string lineText = editor.Document.GetText(curLine.Offset, curLine.Length);
			ILexer lexer = ParserFactory.CreateLexer(SupportedLanguage.VBNet, new System.IO.StringReader(lineText));
			if (lexer.NextToken().Kind != VBTokens.Dim)
				return false;
			if (lexer.NextToken().Kind != VBTokens.Identifier)
				return false;
			if (lexer.NextToken().Kind != VBTokens.As)
				return false;
			Token t1 = lexer.NextToken();
			if (t1.Kind != VBTokens.QuestionMark)
				return false;
			Token t2 = lexer.NextToken();
			if (t2.Kind != VBTokens.Assign)
				return false;
			string expr = lineText.Substring(t2.Location.Column);
			LoggingService.Debug("DeclarationTypeInference: >" + expr + "<");
			ResolveResult rr = ParserService.Resolve(new ExpressionResult(expr),
			                                         editor.Caret.Line,
			                                         t2.Location.Column, editor.FileName,
			                                         editor.Document.Text);
			if (rr != null && rr.ResolvedType != null) {
				ClassFinder context = new ClassFinder(ParserService.GetParseInformation(editor.FileName), editor.Caret.Line, t1.Location.Column);
				VBNetAmbience ambience = new VBNetAmbience();
				if (CodeGenerator.CanUseShortTypeName(rr.ResolvedType, context))
					ambience.ConversionFlags = ConversionFlags.None;
				else
					ambience.ConversionFlags = ConversionFlags.UseFullyQualifiedTypeNames;
				string typeName = ambience.Convert(rr.ResolvedType);
				using (editor.Document.OpenUndoGroup()) {
					int offset = curLine.Offset + t1.Location.Column - 1;
					editor.Document.Remove(offset, 1);
					editor.Document.Insert(offset, typeName);
				}
				editor.Caret.Column += typeName.Length - 1;
				return true;
			}
			return false;
		}
	}
}
