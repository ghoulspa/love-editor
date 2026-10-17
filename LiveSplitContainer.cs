using System;
using System.Drawing;
using System.Windows.Forms;

namespace LoveEditor;

sealed class LiveSplitContainer : SplitContainer, IMessageFilter
{
    const int WmKeyDown = 0x0100;
    bool dragging;
    int dragOffset;
    int dragStartDistance;

    public event EventHandler? SplitterCommitted;
    public event EventHandler? ResetRequested;

    public LiveSplitContainer()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    Cursor ResizeCursor => Orientation == Orientation.Vertical ? Cursors.SizeWE : Cursors.SizeNS;

    int Along(Point point) => Orientation == Orientation.Vertical ? point.X : point.Y;

    int Length => Orientation == Orientation.Vertical ? Width : Height;

    bool OverSplitter(Point point) => !IsSplitterFixed && !Panel1Collapsed && !Panel2Collapsed && SplitterRectangle.Contains(point);

    public void MoveTo(int distance)
    {
        int max = Length - SplitterWidth - Panel2MinSize;
        if (max < Panel1MinSize) return;
        int value = Math.Max(Panel1MinSize, Math.Min(max, distance));
        if (value != SplitterDistance) SplitterDistance = value;
    }

    public int Panel2Size => Length - SplitterDistance - SplitterWidth;

    public void SetLimits(int panel1, int panel2)
    {
        if (Length < panel1 + panel2 + SplitterWidth) return;
        try
        {
            Panel1MinSize = panel1;
            Panel2MinSize = panel2;
        }
        catch (InvalidOperationException)
        {
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && OverSplitter(e.Location))
        {
            dragging = true;
            dragStartDistance = SplitterDistance;
            dragOffset = Along(e.Location) - SplitterDistance;
            Application.AddMessageFilter(this);
            Capture = true;
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
        {
            MoveTo(Along(e.Location) - dragOffset);
            Parent?.Update();
            return;
        }
        Cursor = OverSplitter(e.Location) ? ResizeCursor : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (dragging)
        {
            EndDrag();
            SplitterCommitted?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (dragging && !Capture)
        {
            EndDrag();
            SplitterCommitted?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!dragging) Cursor = Cursors.Default;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && OverSplitter(e.Location))
        {
            ResetRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnMouseDoubleClick(e);
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (dragging && m.Msg == WmKeyDown && (Keys)(int)m.WParam == Keys.Escape)
        {
            EndDrag();
            MoveTo(dragStartDistance);
            return true;
        }
        return false;
    }

    void EndDrag()
    {
        dragging = false;
        Application.RemoveMessageFilter(this);
        Capture = false;
        Cursor = Cursors.Default;
    }
}
