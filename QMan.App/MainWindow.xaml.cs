using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using QMan.Data;

namespace QMan.App;

public partial class MainWindow : Window
{
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
        EnsureDefaultCategory();
        RefreshStatusVec();
        RefreshChatCategories();
        RefreshManageCategories();
        RefreshDocumentsGrid();
    }

    private void EnsureDefaultCategory()
    {
        var ctx = AppContextRoot.Instance;
        if (ctx.Categories.ListAll().Count == 0)
            ctx.Categories.Create("일반");
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        AppContextRoot.Shutdown();
    }

    private void RefreshStatusVec()
    {
        var ctx = AppContextRoot.Instance;
        var dll = ctx.Config.SqliteVecDllPath ?? "";
        var dllName = string.IsNullOrWhiteSpace(dll) ? "(미설정)" : System.IO.Path.GetFileName(dll);
        StatusVec.Text = ctx.Db.VecEnabled
            ? $"sqlite-vec: ON ({dllName})"
            : $"sqlite-vec: OFF (fallback) ({dllName})";
    }

    private void RefreshChatCategories()
    {
        var ctx = AppContextRoot.Instance;
        var items = ctx.Categories.ListAll()
            .Select(c => new CategoryItem { Id = c.Id, Name = c.Name })
            .ToList();
        ChatCategoryList.ItemsSource = items;
        if (items.Count > 0 && ChatCategoryList.SelectedIndex < 0)
            ChatCategoryList.SelectedIndex = 0;
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

        ChatInput.Clear();
        AppendBubble("나", text, true);

        long? categoryId = null;
        if (ChatCategoryList.SelectedItem is CategoryItem c)
            categoryId = c.Id;

        ChatSendButton.IsEnabled = false;
        ChatSendButton.Content = "답변중";
        try
        {
            var ctx = AppContextRoot.Instance;
            var answer = await ctx.Rag.AnswerAsync(text, categoryId).ConfigureAwait(true);
            AppendBubble("Q-Man", answer, false);
        }
        catch (Exception ex)
        {
            AppendBubble("Q-Man", "오류: " + ex.Message, false);
        }
        finally
        {
            ChatSendButton.IsEnabled = true;
            ChatSendButton.Content = "전송";
            ChatInput.Focus();
        }
    }

    private void AppendBubble(string who, string body, bool isUser)
    {
        var muted = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        var textFg = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));

        var header = new TextBlock
        {
            Text = $"{who} · {DateTime.Now:yyyy-MM-dd HH:mm}",
            Foreground = muted,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 2, 6)
        };

        var tb = new TextBox
        {
            Text = body,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = textFg,
            FontSize = 14,
            CaretBrush = textFg
        };

        var bubbleBg = isUser
            ? new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        var bubbleBorder = isUser
            ? new SolidColorBrush(Color.FromRgb(0x81, 0x84, 0xF8))
            : new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));

        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(6, 6, 6, 6),
            MaxWidth = 640,
            Background = bubbleBg,
            BorderBrush = bubbleBorder,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { header, tb } }
        };

        var row = new DockPanel
        {
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        row.Children.Add(border);
        MessagesPanel.Children.Add(row);
        ChatScroll.ScrollToEnd();
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

    private void RefreshDocs_OnClick(object sender, RoutedEventArgs e) => RefreshDocumentsGrid();

    private void ManageCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshDocumentsGrid();
}
