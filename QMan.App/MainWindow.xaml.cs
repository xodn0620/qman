using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using QMan.Data;

namespace QMan.App;

public partial class MainWindow : Window
{
    private static readonly Regex RefDocSuffix = new(@"\n\n\[ 참조문서:\s*(.+?)\s*\]\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private sealed class ChatTurn
    {
        public string Who { get; init; } = "";
        public string Body { get; init; } = "";
        public bool IsUser { get; init; }
        public DateTime Time { get; init; }
    }

    private readonly Dictionary<long, List<ChatTurn>> _chatSessions = new();

    public sealed class CategoryItem
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
    }

    public sealed class DocumentRow
    {
        public long Id { get; init; }
        public string OriginalName { get; init; } = "";
        public string UploadedAt { get; init; } = "";
        public string SizeText { get; init; } = "";
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_OnLoaded;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var ctx = AppContextRoot.Instance;
        if (ctx.NeedsInitialSetup)
        {
            var w = new SettingsWindow { Owner = this, FirstRun = true };
            if (w.ShowDialog() == true)
            {
                AppRestartHelper.Restart();
                return;
            }

            Application.Current.Shutdown();
            return;
        }

        RefreshStatusVec();
        RefreshChatCategories();
        RefreshManageCategories();
        RefreshDocumentsGrid();
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        AppContextRoot.Shutdown();
    }

    private void RefreshStatusVec()
    {
        var ctx = AppContextRoot.Instance;
        if (ctx.Db.VecEnabled)
        {
            StatusVec.Text = "벡터 검색: 사용 (native)";
            StatusVec.ToolTip = null;
            return;
        }

        var why = ctx.Db.VecDisabledReason;
        if (string.IsNullOrWhiteSpace(why))
        {
            StatusVec.Text = "벡터 검색: 사용 안 함";
            StatusVec.ToolTip = null;
            return;
        }

        var one = why.ReplaceLineEndings(" ").Trim();
        const int max = 72;
        var shortWhy = one.Length <= max ? one : one[..(max - 1)] + "…";
        StatusVec.Text = "벡터 검색: 사용 안 함 — " + shortWhy;
        StatusVec.ToolTip = why;
    }

    private void StatusSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow { Owner = this, FirstRun = false };
        if (w.ShowDialog() != true)
            return;

        MessageBox.Show(this,
            "설정이 저장되었습니다. 적용을 위해 앱을 다시 시작합니다.",
            "설정",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        AppRestartHelper.Restart();
    }

    private const string DragFormatChatCategory = "QManCategoryItem";

    private CategoryItem? _chatCategoryDragMouseDownItem;
    private Point _chatCategoryDragStart;

    private void RefreshChatCategories(long? reselectCategoryId = null)
    {
        var ctx = AppContextRoot.Instance;
        var items = ctx.Categories.ListAll()
            .Select(c => new CategoryItem { Id = c.Id, Name = c.Name })
            .ToList();
        ChatCategoryList.ItemsSource = items;
        var empty = items.Count == 0;
        ChatCategoryScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ChatCategoryEmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count > 0)
        {
            CategoryItem? pick = null;
            if (reselectCategoryId is long rid)
                pick = items.FirstOrDefault(c => c.Id == rid);
            if (pick is not null)
                ChatCategoryList.SelectedItem = pick;
            else if (ChatCategoryList.SelectedIndex < 0)
                ChatCategoryList.SelectedIndex = 0;
        }

        ApplyChatComposerEnabledState();
        LoadChatSessionForSelectedCategory();
    }

    private void ChatClear_OnClick(object sender, RoutedEventArgs e)
    {
        if (ChatCategoryList.SelectedItem is not CategoryItem c)
            return;

        if (!_chatSessions.TryGetValue(c.Id, out var list) || list.Count == 0)
        {
            MessageBox.Show(this, "지울 대화가 없습니다.", "대화 지우기",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var r = MessageBox.Show(this,
            $"「{c.Name}」 카테고리의 채팅 기록을 모두 지울까요?\n\n매뉴얼 문서는 삭제되지 않습니다.",
            "대화 지우기",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (r != MessageBoxResult.Yes)
            return;

        list.Clear();
        LoadChatSessionForSelectedCategory();
    }

    private void ChatCategoryList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadChatSessionForSelectedCategory();
    }

    private void ChatCategoryList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _chatCategoryDragMouseDownItem =
            CategoryItemFromListBoxPoint(ChatCategoryList, e.GetPosition(ChatCategoryList));
        _chatCategoryDragStart = e.GetPosition(null);
    }

    private void ChatCategoryList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _chatCategoryDragMouseDownItem = null;
    }

    private void ChatCategoryList_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_chatCategoryDragMouseDownItem is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var cur = e.GetPosition(null);
        if (Math.Abs(cur.X - _chatCategoryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(cur.Y - _chatCategoryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var payload = _chatCategoryDragMouseDownItem;
        _chatCategoryDragMouseDownItem = null;
        try
        {
            DragDrop.DoDragDrop(ChatCategoryList, new DataObject(DragFormatChatCategory, payload),
                DragDropEffects.Move);
        }
        catch
        {
            // 사용자가 ESC 등으로 취소한 경우
        }
        finally
        {
            HideChatCategoryDropIndicator();
        }
    }

    private void ChatCategoryList_OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DragFormatChatCategory))
        {
            e.Effects = DragDropEffects.None;
            HideChatCategoryDropIndicator();
            return;
        }

        var dragged = e.Data.GetData(DragFormatChatCategory) as CategoryItem;
        if (dragged is null)
        {
            e.Effects = DragDropEffects.None;
            HideChatCategoryDropIndicator();
            return;
        }

        var list = ChatCategoryList.Items.Cast<CategoryItem>().ToList();
        if (!ComputeChatCategoryInsertAt(ChatCategoryList, list, dragged, e, out var insertAt, out var fromIdx))
        {
            e.Effects = DragDropEffects.None;
            HideChatCategoryDropIndicator();
            return;
        }

        if (insertAt == fromIdx || insertAt == fromIdx + 1)
        {
            e.Effects = DragDropEffects.Move;
            HideChatCategoryDropIndicator();
            return;
        }

        e.Effects = DragDropEffects.Move;
        UpdateChatCategoryDropIndicator(insertAt, list, e.GetPosition(ChatCategoryList).Y);
    }

    private void ChatCategoryList_OnDragLeave(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormatChatCategory))
            return;
        HideChatCategoryDropIndicator();
    }

    private void ChatCategoryList_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        HideChatCategoryDropIndicator();
        if (!e.Data.GetDataPresent(DragFormatChatCategory))
            return;

        var dragged = e.Data.GetData(DragFormatChatCategory) as CategoryItem;
        if (dragged is null)
            return;

        var list = ChatCategoryList.Items.Cast<CategoryItem>().ToList();
        if (!ComputeChatCategoryInsertAt(ChatCategoryList, list, dragged, e, out var insertAt, out var fromIdx))
            return;

        if (insertAt == fromIdx || insertAt == fromIdx + 1)
            return;

        if (fromIdx < insertAt)
            insertAt--;

        list.RemoveAt(fromIdx);
        insertAt = Math.Clamp(insertAt, 0, list.Count);
        list.Insert(insertAt, dragged);

        try
        {
            AppContextRoot.Instance.Categories.SetSortOrder(list.Select(x => x.Id).ToList());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "순서 변경 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var keepManageId = ManageCategoryList.SelectedItem is CategoryItem mc ? mc.Id : (long?)null;
        RefreshChatCategories(dragged.Id);
        RefreshManageCategories();
        if (keepManageId is long km)
        {
            foreach (CategoryItem c in ManageCategoryList.Items)
            {
                if (c.Id != km)
                    continue;
                ManageCategoryList.SelectedItem = c;
                break;
            }
        }
    }

    /// <summary>
    /// 드래그한 항목을 제거하기 전 기준으로, 놓았을 때 삽입될 인덱스(0..Count)를 계산합니다.
    /// 자기 자신 위에만 있을 때 등 유효하지 않으면 false.
    /// </summary>
    private static bool ComputeChatCategoryInsertAt(
        ListBox lb,
        List<CategoryItem> list,
        CategoryItem dragged,
        DragEventArgs e,
        out int insertAt,
        out int fromIdx)
    {
        insertAt = 0;
        fromIdx = list.FindIndex(x => x.Id == dragged.Id);
        if (fromIdx < 0)
            return false;

        var pos = e.GetPosition(lb);
        var targetLbi = ListBoxItemFromPoint(lb, pos);
        if (targetLbi?.Content is CategoryItem over)
        {
            if (over.Id == dragged.Id)
                return false;
            insertAt = list.FindIndex(x => x.Id == over.Id);
            if (insertAt < 0)
                return false;
            var relY = e.GetPosition(targetLbi).Y;
            if (relY > targetLbi.ActualHeight / 2)
                insertAt++;
        }
        else
            insertAt = list.Count;

        return true;
    }

    private void UpdateChatCategoryDropIndicator(int insertAt, IReadOnlyList<CategoryItem> list,
        double pointerYInListBox)
    {
        ChatCategoryList.UpdateLayout();
        var lineY = GetChatCategoryDropLineYInListBox(insertAt, list, pointerYInListBox);
        var lb = ChatCategoryList;
        var overlay = ChatCategoryDropOverlay;

        var leftTop = lb.TranslatePoint(new Point(0, lineY), overlay);
        var rightTop = lb.TranslatePoint(new Point(Math.Max(1, lb.ActualWidth), lineY), overlay);
        var w = Math.Max(8, rightTop.X - leftTop.X);

        Canvas.SetLeft(ChatCategoryDropIndicator, Math.Max(0, leftTop.X));
        Canvas.SetTop(ChatCategoryDropIndicator, Math.Max(0, leftTop.Y - 1));
        ChatCategoryDropIndicator.Width = w;
        ChatCategoryDropIndicator.Visibility = Visibility.Visible;
    }

    private double GetChatCategoryDropLineYInListBox(int insertAt, IReadOnlyList<CategoryItem> list,
        double pointerYInListBox)
    {
        var gen = ChatCategoryList.ItemContainerGenerator;

        if (list.Count == 0)
            return 0;

        if (insertAt <= 0)
        {
            if (gen.ContainerFromIndex(0) is ListBoxItem first)
            {
                var p = first.TranslatePoint(new Point(0, 0), ChatCategoryList);
                return p.Y;
            }

            return pointerYInListBox;
        }

        if (insertAt >= list.Count)
        {
            var lastIdx = list.Count - 1;
            if (gen.ContainerFromIndex(lastIdx) is ListBoxItem last)
            {
                var h = last.ActualHeight > 0 ? last.ActualHeight : 24;
                var p = last.TranslatePoint(new Point(0, h), ChatCategoryList);
                return p.Y;
            }

            return pointerYInListBox;
        }

        if (gen.ContainerFromIndex(insertAt) is ListBoxItem item)
        {
            var p = item.TranslatePoint(new Point(0, 0), ChatCategoryList);
            return p.Y;
        }

        return pointerYInListBox;
    }

    private void HideChatCategoryDropIndicator()
    {
        ChatCategoryDropIndicator.Visibility = Visibility.Collapsed;
    }

    private static CategoryItem? CategoryItemFromListBoxPoint(ListBox lb, Point positionRelativeToListBox)
    {
        var lbi = ListBoxItemFromPoint(lb, positionRelativeToListBox);
        return lbi?.Content as CategoryItem;
    }

    private static ListBoxItem? ListBoxItemFromPoint(ListBox lb, Point positionRelativeToListBox)
    {
        var hit = VisualTreeHelper.HitTest(lb, positionRelativeToListBox);
        if (hit is null)
            return null;

        for (var v = hit.VisualHit as DependencyObject;
             v is not null;
             v = VisualTreeHelper.GetParent(v))
        {
            if (v is ListBoxItem lbi)
                return lbi;
        }

        return null;
    }

    private void LoadChatSessionForSelectedCategory()
    {
        MessagesPanel.Children.Clear();
        if (ChatCategoryList.SelectedItem is CategoryItem c &&
            _chatSessions.TryGetValue(c.Id, out var turns))
        {
            foreach (var m in turns)
                RenderChatBubble(m.Who, m.Body, m.IsUser, m.Time);
        }

        RefreshChatEmptyState();
        ChatScroll.ScrollToEnd();
        UpdateChatClearButtonState();
    }

    private void ApplyChatComposerEnabledState()
    {
        var has = AppContextRoot.Instance.Categories.ListAll().Count > 0;
        ChatInput.IsEnabled = has;
        var busy = string.Equals(ChatSendButton.Content?.ToString(), "답변중", StringComparison.Ordinal);
        ChatSendButton.IsEnabled = has && !busy;
        UpdateChatClearButtonState(busy);
    }

    private void UpdateChatClearButtonState(bool composerBusy = false)
    {
        var hasCats = AppContextRoot.Instance.Categories.ListAll().Count > 0;
        if (!hasCats || ChatCategoryList.SelectedItem is not CategoryItem c)
        {
            ChatClearButton.IsEnabled = false;
            return;
        }

        var hasHistory = _chatSessions.TryGetValue(c.Id, out var list) && list.Count > 0;
        ChatClearButton.IsEnabled = hasHistory && !composerBusy;
    }

    private void RefreshManageCategories()
    {
        var ctx = AppContextRoot.Instance;
        var items = ctx.Categories.ListAll()
            .Select(c => new CategoryItem { Id = c.Id, Name = c.Name })
            .ToList();
        ManageCategoryList.ItemsSource = items;
        if (items.Count > 0 && ManageCategoryList.SelectedIndex < 0)
            ManageCategoryList.SelectedIndex = 0;
    }

    private void RefreshDocumentsGrid()
    {
        var ctx = AppContextRoot.Instance;
        if (ManageCategoryList.SelectedItem is not CategoryItem cat)
        {
            DocumentsGrid.ItemsSource = Array.Empty<DocumentRow>();
            return;
        }

        var rows = ctx.Documents.ListAll(cat.Id)
            .Select(d => new DocumentRow
            {
                Id = d.Id,
                OriginalName = d.OriginalName,
                UploadedAt = d.UploadedAt,
                SizeText = FormatSize(d.SizeBytes)
            })
            .ToList();
        DocumentsGrid.ItemsSource = rows;
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes is null) return "-";
        var b = (double)bytes.Value;
        if (b < 1024) return bytes + " B";
        var kb = b / 1024;
        if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        var mb = kb / 1024;
        if (mb < 1024) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return (mb / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }

    private void ChatInput_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
            return;

        // WPF: 물리 Enter는 보통 Key.Return. 한글 IME 확정 시 Key.ImeProcessed.
        var isSend = e.Key == Key.Return
                     || e.Key == Key.Enter
                     || (e.Key == Key.ImeProcessed && e.ImeProcessedKey == Key.Return);
        if (!isSend)
            return;

        e.Handled = true;
        _ = SendChatAsync();
    }

    private async void ChatSend_OnClick(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async Task SendChatAsync()
    {
        var text = ChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (AppContextRoot.Instance.Categories.ListAll().Count == 0) return;

        long? categoryId = ChatCategoryList.SelectedItem is CategoryItem c ? c.Id : null;

        ChatInput.Clear();
        AppendBubble("나", text, true, categoryId);

        ChatSendButton.IsEnabled = false;
        ChatSendButton.Content = "답변중";
        try
        {
            var ctx = AppContextRoot.Instance;
            if (categoryId is long cid && ctx.Documents.ListAll(cid).Count == 0)
            {
                AppendBubble("Q-Man",
                    "업로드한 매뉴얼이 존재하지 않습니다. \"매뉴얼 관리\" 탭에서 해당 카테고리에 문서를 업로드 해주세요.",
                    false,
                    categoryId);
            }
            else
            {
                var answer = await ctx.Rag.AnswerAsync(text, categoryId).ConfigureAwait(true);
                AppendBubble("Q-Man", answer, false, categoryId);
            }
        }
        catch (Exception ex)
        {
            AppendBubble("Q-Man", "오류: " + ex.Message, false, categoryId);
        }
        finally
        {
            ChatSendButton.Content = "전송";
            ApplyChatComposerEnabledState();
            if (ChatInput.IsEnabled)
                ChatInput.Focus();
        }
    }

    private void AppendBubble(string who, string body, bool isUser, long? historyCategoryId = null)
    {
        var t = DateTime.Now;
        if (historyCategoryId is long hid)
        {
            if (!_chatSessions.TryGetValue(hid, out var list))
            {
                list = new List<ChatTurn>();
                _chatSessions[hid] = list;
            }

            list.Add(new ChatTurn { Who = who, Body = body, IsUser = isUser, Time = t });
        }

        var viewingId = ChatCategoryList.SelectedItem is CategoryItem v ? v.Id : (long?)null;
        if (historyCategoryId is null || (viewingId is long vid && vid == historyCategoryId))
        {
            RenderChatBubble(who, body, isUser, t);
            RefreshChatEmptyState();
            ChatScroll.ScrollToEnd();
        }
    }

    private void RenderChatBubble(string who, string body, bool isUser, DateTime time)
    {
        var muted = new SolidColorBrush(Color.FromRgb(0x5A, 0x6D, 0x85));
        var userHeaderFg = new SolidColorBrush(Color.FromRgb(0xC8, 0xDD, 0xF5));
        var assistantTextFg = new SolidColorBrush(Color.FromRgb(0x14, 0x2B, 0x45));
        var userTextFg = Brushes.White;

        var header = new TextBlock
        {
            Text = $"{who} · {time:yyyy-MM-dd HH:mm}",
            Foreground = isUser ? userHeaderFg : muted,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 2, 6)
        };

        var mainText = body;
        string? refDocLine = null;
        if (!isUser)
        {
            var rm = RefDocSuffix.Match(body);
            if (rm.Success)
            {
                mainText = body[..rm.Index].TrimEnd();
                refDocLine = "참조문서: " + rm.Groups[1].Value.Trim();
            }
        }

        var tb = new TextBox
        {
            Text = mainText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = isUser ? userTextFg : assistantTextFg,
            FontSize = 14,
            CaretBrush = isUser ? userTextFg : assistantTextFg
        };

        UIElement bodyBlock;
        if (refDocLine is not null)
        {
            var refTb = new TextBlock
            {
                Text = refDocLine,
                FontSize = 11,
                Foreground = muted,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 8, 2, 0),
                MaxWidth = 600
            };
            bodyBlock = new StackPanel { Children = { tb, refTb } };
        }
        else
            bodyBlock = tb;

        var bubbleBg = isUser
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x4A, 0x9F))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        var bubbleBorder = isUser
            ? new SolidColorBrush(Color.FromRgb(0x0B, 0x6F, 0xDE))
            : new SolidColorBrush(Color.FromRgb(0xC5, 0xD4, 0xE8));

        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(6, 6, 6, 6),
            MaxWidth = 640,
            Background = bubbleBg,
            BorderBrush = bubbleBorder,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { header, bodyBlock } }
        };

        var row = new DockPanel
        {
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        row.Children.Add(border);
        MessagesPanel.Children.Add(row);
    }

    private void RefreshChatEmptyState()
    {
        ChatEmptyState.Visibility = MessagesPanel.Children.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void CategoryAdd_OnClick(object sender, RoutedEventArgs e)
    {
        var name = TextInputDialog.Show(this, "카테고리 추가", "카테고리 이름:");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            AppContextRoot.Instance.Categories.Create(name.Trim());
            RefreshChatCategories();
            RefreshManageCategories();
            RefreshDocumentsGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CategoryRename_OnClick(object sender, RoutedEventArgs e)
    {
        if (ManageCategoryList.SelectedItem is not CategoryItem sel) return;
        var name = TextInputDialog.Show(this, "카테고리 수정", "새 이름:", sel.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            AppContextRoot.Instance.Categories.Rename(sel.Id, name.Trim());
            RefreshChatCategories();
            RefreshManageCategories();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CategoryDelete_OnClick(object sender, RoutedEventArgs e)
    {
        if (ManageCategoryList.SelectedItem is not CategoryItem sel) return;
        var ctx = AppContextRoot.Instance;
        var docs = ctx.Documents.ListAll(sel.Id);
        if (MessageBox.Show($"카테고리 '{sel.Name}' 및 문서 {docs.Count}건을 삭제할까요?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            foreach (var d in docs)
                ctx.Ingestion.DeleteDocument(d.Id);
            ctx.Categories.Delete(sel.Id);
            _chatSessions.Remove(sel.Id);
            RefreshChatCategories();
            RefreshManageCategories();
            RefreshDocumentsGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Upload_OnClick(object sender, RoutedEventArgs e)
    {
        if (ManageCategoryList.SelectedItem is not CategoryItem cat)
        {
            MessageBox.Show("카테고리를 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "문서 업로드",
            Filter = "문서|*.pdf;*.pptx;*.ppt;*.docx;*.doc;*.xlsx;*.xls;*.txt|이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff|모든 파일|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        var categoryId = cat.Id;

        UploadProgress.Visibility = Visibility.Visible;
        UploadStatus.Text = "업로드/인덱싱 중...";
        try
        {
            var ctx = AppContextRoot.Instance;
            var res = ctx.Ingestion.Ingest(categoryId, path);
            var total = res.ChunkIds.Count;
            for (var i = 0; i < total; i++)
            {
                var chunkId = res.ChunkIds[i];
                var chunk = ctx.Chunks.FindById(chunkId);
                var emb = await ctx.Llm.EmbedAsync(chunk.Content).ConfigureAwait(true);
                ctx.Rag.IndexChunkEmbedding(chunkId, emb);
                UploadStatus.Text = $"임베딩 {i + 1}/{total}";
                await System.Threading.Tasks.Task.Yield();
            }

            RefreshDocumentsGrid();
            RefreshStatusVec();
            MessageBox.Show("인덱싱이 완료되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "업로드 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UploadProgress.Visibility = Visibility.Collapsed;
            UploadStatus.Text = "";
        }
    }

    private void DeleteDoc_OnClick(object sender, RoutedEventArgs e)
    {
        if (DocumentsGrid.SelectedItem is not DocumentRow row) return;
        if (MessageBox.Show($"문서를 삭제할까요?\n{row.OriginalName}", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            AppContextRoot.Instance.Ingestion.DeleteDocument(row.Id);
            RefreshDocumentsGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ManageCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshDocumentsGrid();
}
