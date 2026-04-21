using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using QMan.Core;
using QMan.Data;

namespace QMan.App;

public partial class SettingsWindow : Window
{
    private bool _uiReady;
    private bool _allowClose;
    private Dictionary<string, LlmProviderFormState> _stateByTag = new(StringComparer.OrdinalIgnoreCase);
    private string _loadedProfileTag = "openai";

    public bool FirstRun { get; init; }

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += SettingsWindow_OnLoaded;
    }

    private void SettingsWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        FirstRunNotice.Visibility = FirstRun ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = FirstRun ? Visibility.Collapsed : Visibility.Visible;

        var ctx = AppContextRoot.Instance;
        var kv = ctx.Settings.LoadAll();
        _stateByTag = AppSettingsDao.LoadProviderFormStates(kv);

        var storedProv = AppConfig.ParseLlmProvider(
            kv.TryGetValue(AppSettingsKeys.LlmProviderKey, out var pt) ? pt : "openai");
        _loadedProfileTag = AppSettingsKeys.ProviderTag(storedProv);

        _uiReady = false;
        LoadStateToForm(_loadedProfileTag);
        _uiReady = true;
        RefreshInferredPanels();
    }

    private void FormInferenceFields_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady)
            return;
        RefreshInferredPanels();
    }

    private void RefreshInferredPanels()
    {
        var p = LlmEndpointInference.Infer(
            UrlBox.Text.Trim(),
            ApiKeyBox.Text.Trim(),
            EmbeddingApiKeyBox.Text.Trim());

        PanelEmbeddingApiKey.Visibility = p == LlmProvider.Claude ? Visibility.Visible : Visibility.Collapsed;

        InferenceHint.Text = p switch
        {
            LlmProvider.Ollama =>
                "추론: Ollama — URL 비우면 http://localhost:11434 , API 키 없이 로컬 사용 가능.",
            LlmProvider.OpenAi =>
                "추론: OpenAI 호환 — URL 비우면 https://api.openai.com/v1 , 채팅·임베딩에 API 키가 필요합니다.",
            LlmProvider.Claude =>
                "추론: Anthropic — 채팅 키(sk-ant…)와 OpenAI 임베딩 키가 필요합니다.",
            LlmProvider.GoogleAi =>
                "추론: Google AI — API 키가 필요합니다.",
            LlmProvider.AlibabaCloud =>
                "추론: DashScope — API 키가 필요합니다.",
            _ => ""
        };
    }

    private void LoadStateToForm(string tag)
    {
        var s = _stateByTag[tag];
        ChatModelBox.Text = s.ChatModel;
        EmbeddingModelBox.Text = s.EmbeddingModel;
        UrlBox.Text = s.Url;
        ApiKeyBox.Text = s.MainApiKey;
        EmbeddingApiKeyBox.Text = s.ClaudeEmbeddingApiKey;
    }

    private void FlushFormToState(string tag)
    {
        var s = _stateByTag[tag];
        s.ChatModel = ChatModelBox.Text.Trim();
        s.EmbeddingModel = EmbeddingModelBox.Text.Trim();
        s.Url = UrlBox.Text.Trim();
        s.MainApiKey = ApiKeyBox.Text.Trim();
        s.ClaudeEmbeddingApiKey = EmbeddingApiKeyBox.Text.Trim();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var ctx = AppContextRoot.Instance;

        var inferred = LlmEndpointInference.Infer(
            UrlBox.Text.Trim(),
            ApiKeyBox.Text.Trim(),
            EmbeddingApiKeyBox.Text.Trim());
        var tag = AppSettingsKeys.ProviderTag(inferred);

        FlushFormToState(tag);

        var active = _stateByTag[tag];
        if (string.IsNullOrWhiteSpace(active.ChatModel) || string.IsNullOrWhiteSpace(active.EmbeddingModel))
        {
            MessageBox.Show(this, "LLM 모델과 임베딩 모델을 입력해 주세요.", "설정",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            ctx.Settings.SaveAllProviderProfiles(_stateByTag, tag, markSetupComplete: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _allowClose = true;
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        DialogResult = false;
    }

    private void SettingsWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        if (!FirstRun)
            return;
        e.Cancel = true;
        MessageBox.Show(this, "「저장」으로 설정을 완료해 주세요.", "설정",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
