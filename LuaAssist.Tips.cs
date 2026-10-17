using System;
using System.Drawing;
using System.Linq;
using System.Text;
using ScintillaNET;

namespace LoveEditor;

partial class LuaAssist
{
    void PostSignature()
    {
        if (signaturePosted || !editor.IsHandleCreated) return;
        signaturePosted = true;
        editor.BeginInvoke(new Action(() =>
        {
            signaturePosted = false;
            if (tip == Tip.Signature) RequestSignature();
        }));
    }

    void ResumeSignature()
    {
        if (!resumeSignature || !editor.IsHandleCreated) return;
        resumeSignature = false;
        editor.BeginInvoke(new Action(RequestSignature));
    }

    void RequestSignature()
    {
        if (!opened || language == null) return;
        Flush();
        int serial = ++signatureSerial;
        int caret = editor.CurrentPosition;
        language.Request("textDocument/signatureHelp", new { textDocument = new { uri }, position = Position(caret) }, result =>
        {
            if (serial == signatureSerial && !editor.IsDisposed) ShowSignature(result, caret);
        });
    }

    void ShowSignature(object? result, int caret)
    {
        if (editor.AutoCActive)
        {
            resumeSignature = true;
            return;
        }
        var signatures = Json.Items(result, "signatures").ToList();
        if (signatures.Count == 0)
        {
            if (tip == Tip.Signature) CancelTip();
            return;
        }
        var signature = signatures[Math.Max(0, Math.Min(Json.Int(result, "activeSignature"), signatures.Count - 1))];
        int active = Json.Int(signature, "activeParameter", Json.Int(result, "activeParameter"));
        var label = Json.Str(signature, "label") ?? "";
        int cut = label.StartsWith("function ", StringComparison.Ordinal) ? 9 : 0;
        int start = -1, end = -1;
        var parameters = Json.Items(signature, "parameters").ToList();
        if (active >= 0 && active < parameters.Count)
        {
            var name = Json.Get(parameters[active], "label");
            var offsets = Json.Items(name).ToList();
            if (offsets.Count == 2 && offsets[0] is int from && offsets[1] is int to)
            {
                start = from - cut;
                end = to - cut;
            }
            else if (name is string text && label.IndexOf(text, cut, StringComparison.Ordinal) is int at && at >= 0)
            {
                start = at - cut;
                end = start + text.Length;
            }
        }
        var display = LuaDocs.WrapCode(label.Substring(cut), ref start, ref end);
        var summary = LuaDocs.Summary(Json.Get(signature, "documentation"));
        if (summary.Length > 0) display += "\n" + summary;
        ShowTip(Tip.Signature, CallStart(caret), display, start, end, CodeHighlight, true);
    }

    int CallStart(int caret)
    {
        int lineStart = editor.Lines[editor.LineFromPosition(caret)].Position;
        int depth = 0;
        for (int position = caret - 1; position >= lineStart; position--)
        {
            char c = CharAt(position);
            if (c == ')') depth++;
            else if (c == '(' && depth-- == 0)
            {
                int start = position;
                while (start > lineStart && (IsWordChar(CharAt(start - 1)) || CharAt(start - 1) == '.' || CharAt(start - 1) == ':')) start--;
                return start;
            }
        }
        return caret;
    }

    void OnDwellStart(object? sender, DwellEventArgs e)
    {
        int position = e.Position;
        if (position < 0 || editor.AutoCActive || tip == Tip.Signature) return;
        int serial = ++hoverSerial;
        if (!opened || language == null || !IsWordChar(CharAt(position)))
        {
            ShowHover(null, position);
            return;
        }
        Flush();
        language.Request("textDocument/hover", new { textDocument = new { uri }, position = Position(position) }, result =>
        {
            if (serial == hoverSerial && !editor.IsDisposed) ShowHover(result, position);
        });
    }

    void ShowHover(object? result, int position)
    {
        if (editor.AutoCActive || tip == Tip.Signature) return;
        var here = problems.Where(p => position >= p.Start && position <= p.End).ToList();
        var (hover, codeLength) = LuaDocs.Hover(Json.Get(result, "contents"));
        if (hover.StartsWith("Workspace loading", StringComparison.Ordinal)) hover = "";
        var messages = string.Join("\n", here.Select(p => p.Message).Distinct().SelectMany(LuaDocs.Wrap));
        if (messages.Length == 0 && hover.Length == 0) return;
        var text = messages.Length == 0 ? hover : hover.Length == 0 ? messages : messages + "\n\n" + hover;
        var range = Json.Get(result, "range");
        hoverStart = range != null ? PositionOf(Json.Get(range, "start")) : here.Count > 0 ? here.Min(p => p.Start) : position;
        hoverEnd = range != null ? PositionOf(Json.Get(range, "end")) : here.Count > 0 ? here.Max(p => p.End) : position;
        if (messages.Length > 0) ShowTip(Tip.Hover, hoverStart, text, 0, messages.Length, ProblemHighlight, false);
        else ShowTip(Tip.Hover, hoverStart, text, 0, codeLength, CodeHighlight, false);
    }

    void OnDwellEnd(object? sender, DwellEventArgs e)
    {
        if (tip != Tip.Hover) return;
        if (e.Position >= hoverStart && e.Position <= hoverEnd && e.Position >= 0) return;
        hoverSerial++;
        CancelTip();
    }

    void ShowTip(Tip kind, int position, string text, int start, int end, Color highlight, bool above)
    {
        if (tip != kind || tipPosition != position || tipText != text || !editor.CallTipActive)
        {
            editor.CallTipSetPosition(above);
            editor.CallTipShow(position, text);
            tipText = text;
            tipPosition = position;
        }
        tip = kind;
        editor.CallTipSetForeHlt(highlight);
        if (start >= 0 && end > start && end <= text.Length)
            editor.CallTipSetHlt(Encoding.UTF8.GetByteCount(text.Substring(0, start)), Encoding.UTF8.GetByteCount(text.Substring(0, end)));
        else editor.CallTipSetHlt(0, 0);
    }

    void CancelTip()
    {
        if (tip != Tip.None && editor.CallTipActive) editor.CallTipCancel();
        tip = Tip.None;
        tipText = "";
        tipPosition = -1;
    }
}
