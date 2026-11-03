using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HttpTester.Core;

namespace HttpTester.App.Controls;

/// <summary>保留 TextBox 的选择、复制和滚动，只绘制当前视口的 JSON 颜色。</summary>
public sealed class JsonResponseView : UserControl
{
    private readonly TextBox _editor;
    private readonly SyntaxLayer _layer;
    private IReadOnlyList<JsonToken> _tokens = [];
    public bool IsSyntaxHighlighted { get; private set; }

    public JsonResponseView()
    {
        _editor = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            SelectionOpacity = 0.3, Padding = new Thickness(10, 7, 10, 7)
        };
        _editor.SetBinding(FontFamilyProperty, new Binding(nameof(FontFamily)) { Source = this });
        _editor.SetBinding(FontSizeProperty, new Binding(nameof(FontSize)) { Source = this });
        _layer = new SyntaxLayer(this) { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var panel = new Grid(); panel.Children.Add(_editor); panel.Children.Add(_layer); Content = panel;
        _editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => _layer.InvalidateVisual()));
        _editor.SizeChanged += (_, _) => _layer.InvalidateVisual();
        Loaded += (_, _) => { if (!IsSyntaxHighlighted) _editor.Foreground = Foreground; _layer.InvalidateVisual(); };
    }

    public string Text { get => _editor.Text; set => ShowPlainText(value); }
    public TextWrapping TextWrapping
    {
        get => _editor.TextWrapping;
        set { _editor.TextWrapping = value; _layer.InvalidateVisual(); }
    }
    public void Clear() => ShowPlainText("");
    public void ShowPlainText(string text)
    {
        IsSyntaxHighlighted = false;
        _tokens = [];
        _layer.Visibility = Visibility.Collapsed;
        _editor.Foreground = Foreground;
        _editor.Text = text;
    }
    public void ShowJson(string formattedJson)
    {
        _tokens = JsonSyntax.Tokenize(formattedJson);
        _editor.Text = formattedJson;
        IsSyntaxHighlighted = true;
        _editor.Foreground = Brushes.Transparent;
        _layer.Visibility = Visibility.Visible;
        _layer.InvalidateVisual();
    }

    private sealed class SyntaxLayer(JsonResponseView owner) : FrameworkElement
    {
        private static readonly Brush[] Colors = [Frozen("#526174"), Frozen("#2459B2"), Frozen("#17805A"), Frozen("#B36617"), Frozen("#8653B8"), Frozen("#9C526F")];
        private static Brush Frozen(string color) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; brush.Freeze(); return brush; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (!owner.IsSyntaxHighlighted || owner._editor.LineCount == 0) return;
            var editor = owner._editor;
            var presenter = FindPresenter(editor);
            if (presenter is null) return;
            var clip = presenter.TransformToVisual(this).TransformBounds(new Rect(presenter.RenderSize));
            drawingContext.PushClip(new RectangleGeometry(clip));
            try
            {
                var text = editor.Text;
                var firstLine = Math.Max(0, editor.GetFirstVisibleLineIndex());
                var lastLine = Math.Min(editor.LineCount - 1, editor.GetLastVisibleLineIndex());
                var typeface = new Typeface(owner.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                for (var line = firstLine; line <= lastLine; line++)
                {
                    var lineStart = editor.GetCharacterIndexFromLineIndex(line);
                    var lineEnd = line + 1 < editor.LineCount ? editor.GetCharacterIndexFromLineIndex(line + 1) : text.Length;
                    while (lineEnd > lineStart && text[lineEnd - 1] is '\r' or '\n') lineEnd--;
                    if (lineStart >= lineEnd) continue;
                    var rect = editor.GetRectFromCharacterIndex(lineStart);
                    if (rect.IsEmpty) continue;
                    // 横向滚动也只绘制当前视口中的字符，避免超长字符串产生巨大的绘图对象。
                    var left = editor.GetCharacterIndexFromPoint(new Point(clip.Left, rect.Top + rect.Height / 2), true);
                    var right = editor.GetCharacterIndexFromPoint(new Point(clip.Right, rect.Top + rect.Height / 2), true);
                    var start = Math.Clamp(Math.Min(left, right) - 1, lineStart, lineEnd);
                    var end = Math.Clamp(Math.Max(left, right) + 2, start, lineEnd);
                    if (start > lineStart && start < lineEnd && char.IsLowSurrogate(text[start])) start--;
                    if (end < lineEnd && end > 0 && char.IsHighSurrogate(text[end - 1])) end++;
                    var tokenIndex = FirstToken(owner._tokens, start);
                    while (tokenIndex < owner._tokens.Count)
                    {
                        var token = owner._tokens[tokenIndex++];
                        if (token.Start >= end) break;
                        var segmentStart = Math.Max(start, token.Start);
                        var segmentEnd = Math.Min(end, token.Start + token.Length);
                        if (segmentEnd <= segmentStart) continue;
                        var origin = editor.GetRectFromCharacterIndex(segmentStart);
                        if (origin.IsEmpty) continue;
                        var formatted = new FormattedText(text[segmentStart..segmentEnd], CultureInfo.CurrentUICulture,
                            FlowDirection.LeftToRight, typeface, owner.FontSize, Colors[(int)token.Kind], pixelsPerDip);
                        drawingContext.DrawText(formatted, origin.TopLeft);
                    }
                }
            }
            finally { drawingContext.Pop(); }
        }

        private static int FirstToken(IReadOnlyList<JsonToken> tokens, int character)
        {
            var low = 0; var high = tokens.Count;
            while (low < high)
            { var middle = low + (high - low) / 2; if (tokens[middle].Start + tokens[middle].Length <= character) low = middle + 1; else high = middle; }
            return low;
        }

        private static ScrollContentPresenter? FindPresenter(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollContentPresenter presenter) return presenter;
                if (FindPresenter(child) is { } found) return found;
            }
            return null;
        }
    }
}
