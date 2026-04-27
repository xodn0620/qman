using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using QMan.Core;
using QMan.Data;

namespace QMan.App;

public partial class SettingsWindow : Window
{
    private bool _uiReady;
    private bool _suppressModeEvent;
    private Dictionary<string, LlmProviderFormState> _stateByTag = new(StringComparer.OrdinalIgnoreCase);
    private string _loadedProfileTag = "dsplayground";
    /// <summary>타사 AI 모드에서 편집 중인 프로필 태그(콤보로 DS에 갔다 올 때 복원).</summary>
    private string _thirdPartyFormTag = "openai";

    public bool FirstRun { get; init; }

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += SettingsWindow_OnLoaded;
    }

    private static bool IsDsItemSelected(ComboBox c) => c.SelectedIndex == 1;

    private void SettingsWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        CancelButton.Visibility = FirstRun ? Visibility.Collapsed : Visibility.Visible;

        var ctx = AppContextRoot.Instance;
        var kv = ctx.Settings.LoadAll();
        _stateByTag = AppSettingsDao.LoadProviderFormStates(kv);

        var raw = kv.TryGetValue(AppSettingsKeys.LlmProviderKey, out var pt) ? pt : "dsplayground";
        _loadedProfileTag = AppSettingsKeys.ProviderTag(AppConfig.ParseLlmProvider(raw));
        if (_loadedProfileTag == "dsplayground")
            _thirdPartyFormTag = "openai";
        else
            _thirdPartyFormTag = _loadedProfileTag;

        _suppressModeEvent = true;
        _uiReady = false;
        ModeCombo.SelectedIndex = _loadedProfileTag == "dsplayground" ? 1 : 0;
        var loadTag = IsDsItemSelected(ModeCombo) ? "dsplayground" : _thirdPartyFormTag;
        LoadStateToForm(loadTag);
        _suppressModeEvent = false;
        _uiReady = true;
        RefreshInferredPanels();
    }

    private void ModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressModeEvent)
            return;

        var isDs = IsDsItemSelected(ModeCombo);
        if (e.RemovedItems.Count > 0)
        {
            if (isDs)
            {
                // 타사 -> DS: 이전(타사) 화면 내용을 프로필에 반영
                var inferred = LlmEndpointInference.Infer(
                    UrlBox.Text.Trim(),
                    ApiKeyBox.Text.Trim(),
                    EmbeddingApiKeyBox.Text.Trim());
                var tag = AppSettingsKeys.ProviderTag(inferred);
                FlushFormToState(tag);
                _thirdPartyFormTag = tag;
            }
            else
            {
                // DS -> 타사: DS 화면 내용을 반영
                FlushFormToState("dsplayground");
            }
        }

        var loadTag = isDs ? "dsplayground" : _thirdPartyFormTag;
        LoadStateToForm(loadTag);
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
        if (IsDsItemSelected(ModeCombo))
        {
            FirstRunNotice.Visibility = Visibility.Collapsed;
            LlmModelLabel.Text = "LLM 모델";
            EmbModelLabel.Text = "임베딩 모델";
            PanelEmbeddingApiKey.Visibility = Visibility.Visible;
            EmbeddingKeyTitle.Text = "임베딩 API 키";
            EmbeddingKeyHint.Visibility = Visibility.Collapsed;
            ClaudeEmbeddingKeyHint.Visibility = Visibility.Collapsed;
            ApiKeyLabel.Text = "LLM API 키";
            UrlLabel.Text = "API URL";
            InferenceHint.Visibility = Visibility.Collapsed;
            InferenceHint.Text = string.Empty;
            return;
        }

        FirstRunNotice.Visibility = FirstRun ? Visibility.Visible : Visibility.Collapsed;
        LlmModelLabel.Text = "LLM 모델 (필수)";
        EmbModelLabel.Text = "임베딩 모델 (필수)";
        InferenceHint.Visibility = Visibility.Visible;
        var p = LlmEndpointInference.Infer(
            UrlBox.Text.Trim(),
            ApiKeyBox.Text.Trim(),
            EmbeddingApiKeyBox.Text.Trim());

        var showEmb = p == LlmProvider.Claude;
        PanelEmbeddingApiKey.Visibility = showEmb ? Visibility.Visible : Visibility.Collapsed;
        if (showEmb)
        {
            EmbeddingKeyTitle.Text = "임베딩 API 키 (Claude: OpenAI 호환 키)";
            EmbeddingKeyHint.Visibility = Visibility.Collapsed;
            ClaudeEmbeddingKeyHint.Visibility = Visibility.Visible;
        }
        else
        {
            EmbeddingKeyHint.Visibility = Visibility.Collapsed;
            ClaudeEmbeddingKeyHint.Visibility = Visibility.Collapsed;
        }

        UrlLabel.Text = "API URL (선택 — 비우면 기본값)";

        ApiKeyLabel.Text = p == LlmProvider.Claude
            ? "채팅 API 키 (Claude) / 비우면 환경 변수"
            : "LLM API 키 (선택 — 비우면 환경 변수 또는 로컬 백엔드)";

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
        var isDs = IsDsItemSelected(ModeCombo);

        string activeTag;
        if (isDs)
        {
            FlushFormToState("dsplayground");
            activeTag = "dsplayground";
        }
        else
        {
            var inferred = LlmEndpointInference.Infer(
                UrlBox.Text.Trim(),
                ApiKeyBox.Text.Trim(),
                EmbeddingApiKeyBox.Text.Trim());
            activeTag = AppSettingsKeys.ProviderTag(inferred);
            FlushFormToState(activeTag);
            _thirdPartyFormTag = activeTag;
        }

        if (!_stateByTag.TryGetValue(activeTag, out var active))
        {
            MessageBox.Show(this, "저장할 설정 상태를 찾을 수 없습니다.", "설정", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (isDs)
        {
            if (string.IsNullOrWhiteSpace(active.ChatModel) ||
                string.IsNullOrWhiteSpace(active.EmbeddingModel) ||
                string.IsNullOrWhiteSpace(active.MainApiKey) ||
                string.IsNullOrWhiteSpace(active.ClaudeEmbeddingApiKey) ||
                string.IsNullOrWhiteSpace(active.Url))
            {
                MessageBox.Show(this, "필수 입력 항목을 입력하세요.", "설정",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(active.ChatModel) || string.IsNullOrWhiteSpace(active.EmbeddingModel))
            {
                MessageBox.Show(this, "LLM 모델과 임베딩 모델을 입력해 주세요.", "설정",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            ctx.Settings.SaveAllProviderProfiles(_stateByTag, activeTag, markSetupComplete: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

}
