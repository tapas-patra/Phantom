using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using SecureOverlay.Application.Context;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Services;
using SecureOverlay.Helpers;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Context;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;
using SecureOverlay.Infrastructure.Persistence;

namespace SecureOverlay
{
    public partial class SettingsPage : UserControl
    {
        private const int MaxProvidersForByo = 3;
        private const int MaxKeysPerProvider = 2;
        private AppSettings _settings;
        private readonly IContextPackService _contextPackService;
        private readonly AccountCacheSnapshot? _accountSnapshot;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IHostedAccountClient _hostedAccountClient;
        private bool _isUpdatingSlider = false;
        private bool _isInitializing = true;
        private bool _isUpdatingContextPackSelection;
        private bool _isEditingSelectedHostedPack;
        private bool _lastAppliedSelectionWasLocalDraft = true;
        private bool _localDraftCacheInvalidated;
        private List<DesktopContextPackDto> _hostedContextPacks = new List<DesktopContextPackDto>();

        // API Key collections
        private ObservableCollection<ApiKeyItem> _chatGPTKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _claudeKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _mistralKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _geminiKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _groqKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _nvidiaKeys = new ObservableCollection<ApiKeyItem>();

        public event EventHandler<SettingsCloseResult>? SettingsClosed;

        public SettingsPage(AccountCacheSnapshot? accountSnapshot = null)
        {
            InitializeComponent();

            _settings = SettingsManager.Load();
            _accountSnapshot = accountSnapshot;
            var store = new SqliteRuntimeStore(SettingsManager.GetSettingsPath());
            _contextPackService = new LocalContextPackService(new SqliteContextPackRepository(store));
            _authSessionRepository = new SqliteAuthSessionRepository(store);
            _hostedAccountClient = HostedClientFactory.CreateAccountClient(HostedClientFactory.LoadOptions());

            InitializeControls();
            LoadSettings();
            ApplyAccountTierRestrictions();
            ProtectAllComboBoxes();
            
            _isInitializing = false;
        }

        private void ProtectAllComboBoxes()
        {
            ComboBoxProtection.ProtectComboBox(AIProviderComboBox);
            ComboBoxProtection.ProtectComboBox(ChatGPTModelBox);
            ComboBoxProtection.ProtectComboBox(ClaudeModelBox);
            ComboBoxProtection.ProtectComboBox(MistralModelBox);
            ComboBoxProtection.ProtectComboBox(GeminiModelBox);
            ComboBoxProtection.ProtectComboBox(GroqModelBox);
            ComboBoxProtection.ProtectComboBox(NvidiaModelBox);
            ComboBoxProtection.ProtectComboBox(InterviewTypeComboBox);
            ComboBoxProtection.ProtectComboBox(ManagedModelComboBox);
            ComboBoxProtection.ProtectComboBox(SavedContextPackComboBox);
        }

        private void InitializeControls()
        {
            PopulateProviderChoices();

            foreach (var interviewType in InterviewPromptRegistry.GetAllInterviewTypes())
            {
                InterviewTypeComboBox.Items.Add(interviewType);
            }

            // ✅ USE REGISTRY - ChatGPT Models
            foreach (var model in _settings.ChatGPTModels)
            {
                ChatGPTModelBox.Items.Add(model);
            }

            // ✅ USE REGISTRY - Claude Models
            foreach (var model in _settings.ClaudeModels)
            {
                ClaudeModelBox.Items.Add(model);
            }

            // ✅ USE REGISTRY - Mistral Models
            foreach (var model in _settings.MistralModels)
            {
                MistralModelBox.Items.Add(model);
            }

            // ✅ USE REGISTRY - Gemini Models
            foreach (var model in _settings.GeminiModels)
            {
                GeminiModelBox.Items.Add(model);
            }

            // ✅ USE REGISTRY - Groq Models
            foreach (var model in _settings.GroqModels)
            {
                GroqModelBox.Items.Add(model);
            }

            foreach (var model in _settings.NvidiaModels)
            {
                NvidiaModelBox.Items.Add(model);
            }
        }


        private void LoadSettings()
        {
            PopulateProviderChoices();
            PopulateByoModelChoices();
            AIProviderComboBox.SelectedItem = _settings.SelectedAI;
            if (AIProviderComboBox.SelectedItem == null && AIProviderComboBox.Items.Count > 0)
            {
                AIProviderComboBox.SelectedIndex = 0;
            }
            
            // Load API keys
            LoadApiKeys();
            
            // ✅ FIX: Get CURRENT model from rotation manager (if available), not just from settings
            var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            
            // Load currently ACTIVE model (reflects rotation state)
            string chatGPTModel = _settings.ChatGPTModel;
            string claudeModel = _settings.ClaudeModel;
            string mistralModel = _settings.MistralModel;
            string geminiModel = _settings.GeminiModel;
            string groqModel = _settings.GroqModel;
            string nvidiaModel = _settings.NvidiaModel;
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("LOADING SETTINGS PAGE");
            Log.WriteLine($"  ChatGPT model from settings: {chatGPTModel}");
            Log.WriteLine($"  Claude model from settings: {claudeModel}");
            Log.WriteLine($"  Mistral model from settings: {mistralModel}");
            Log.WriteLine($"  Gemini model from settings: {geminiModel}");
            Log.WriteLine($"  Groq model from settings: {groqModel}");
            Log.WriteLine($"  NVIDIA model from settings: {nvidiaModel}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            // Set selected models in ComboBoxes
            ChatGPTModelBox.SelectedItem = chatGPTModel;
            ClaudeModelBox.SelectedItem = claudeModel;
            MistralModelBox.SelectedItem = mistralModel;
            GeminiModelBox.SelectedItem = geminiModel;
            GroqModelBox.SelectedItem = groqModel;
            NvidiaModelBox.SelectedItem = nvidiaModel;
            PopulateManagedModelChoices();

            VoiceInputCheckBox.IsChecked = _settings.VoiceInputEnabled;
            AutoSendAfterVoiceStopCheckBox.IsChecked = _settings.AutoSendAfterVoiceStopEnabled;
            
            // Rotation settings
            AutoSwitchKeysCheckBox.IsChecked = _settings.AutoSwitchKeysOnError;
            AutoSwitchModelsCheckBox.IsChecked = _settings.AutoSwitchModelsOnError;
            SessionContinuationCheckBox.IsChecked = IsFreeTrialAccount()
                ? _settings.AllowFreeTrialSessionExtension
                : _settings.AllowByoSessionExtension;
            
            UseFakeCursorCheckBox.IsChecked = _settings.UseFakeCursor;
            
            _isUpdatingSlider = true;
            
            double displayValue = _settings.FakeCursorSize * 100;
            if (displayValue < 50) displayValue = 100;
            if (displayValue > 200) displayValue = 100;
            
            FakeCursorSizeSlider.Value = displayValue;
            FakeCursorSizeTextBox.Text = $"{displayValue:F0}";
            
            _isUpdatingSlider = false;
            
            UpdateFakeCursorPanelVisibility();
            InterviewTypeComboBox.SelectedItem = _settings.InterviewPromptType;
            AutoPauseInactivityCheckBox.IsChecked = _settings.AutoPauseOnInactivityEnabled;
            AutoPauseMinutesTextBox.Text = Math.Max(10, _settings.AutoPauseOnInactivityMinutes).ToString();

            LoadContextPackEditors();

            if (DebugModeCheckBox != null)
            {
                DebugModeCheckBox.IsChecked = _settings.DebugModeEnabled;
            }

            if (DebugErrorTypeComboBox != null && !string.IsNullOrWhiteSpace(_settings.DebugErrorSimulation))
            {
                foreach (ComboBoxItem item in DebugErrorTypeComboBox.Items)
                {
                    if (item.Content.ToString() == _settings.DebugErrorSimulation)
                    {
                        DebugErrorTypeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            UpdatePanelVisibility();
        }

        
        public void RefreshSettings()
        {
            Log.WriteLine("Settings page refreshing data...");
            
            // Reload settings from disk
            _settings = SettingsManager.Load();
            
            // Reload all UI elements
            LoadSettings();
            ApplyAccountTierRestrictions();
            
            Log.WriteLine("✓ Settings page refreshed");
        }

        private void LoadContextPackEditors()
        {
            if (IsPremiumAccount())
            {
                LoadHostedContextPacks();
                return;
            }

            ContextPackSection.Visibility = Visibility.Collapsed;
            LoadPackIntoEditors(_contextPackService.GetLocalDraftPack());
        }

        private void LoadHostedContextPacks(string? preferredPackId = null, string? forceSelectedPackId = null)
        {
            ContextPackSection.Visibility = Visibility.Visible;
            ContextPackStatusText.Text = string.Empty;
            var localDraftPack = _contextPackService.GetLocalDraftPack();

            var session = _authSessionRepository.Load();
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                _hostedContextPacks = new List<DesktopContextPackDto>();
                PopulateHostedContextPackChoices(forceSelectedPackId: null);
                LoadPackIntoEditors(localDraftPack);
                ContextPackStatusText.Text = "Sign in again to load Premium context packs.";
                return;
            }

            try
            {
                _hostedContextPacks = _hostedAccountClient.GetContextPacks(session.AccessToken)
                    ?.OrderByDescending(item => item.UpdatedAtUtc)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<DesktopContextPackDto>();

                var selectedPackId = forceSelectedPackId
                    ?? preferredPackId
                    ?? _settings.SelectedHostedContextPackId
                    ?? string.Empty;
                PopulateHostedContextPackChoices(selectedPackId);

                if (_hostedContextPacks.Count == 0)
                {
                    LoadPackIntoEditors(localDraftPack);
                    ContextPackStatusText.Text = "No saved context packs yet. Save one here to reuse your full resume and job description later.";
                }
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted context pack load failed: {ex.Message}");
                PopulateHostedContextPackChoices(forceSelectedPackId: null);
                LoadPackIntoEditors(localDraftPack);
                ContextPackStatusText.Text = $"Could not load Premium context packs right now: {ex.Message}";
            }
        }

        private void PopulateHostedContextPackChoices(string? forceSelectedPackId)
        {
            _isUpdatingContextPackSelection = true;
            try
            {
                SavedContextPackComboBox.Items.Clear();
                SavedContextPackComboBox.Items.Add(ContextPackSelectionItem.CreateBlank());
                foreach (var pack in _hostedContextPacks)
                {
                    SavedContextPackComboBox.Items.Add(new ContextPackSelectionItem
                    {
                        PackId = pack.PackId,
                        DisplayName = pack.Name,
                        IsBlank = false
                    });
                }

                var selectedItem = SavedContextPackComboBox.Items
                    .OfType<ContextPackSelectionItem>()
                    .FirstOrDefault(item => !item.IsBlank && string.Equals(item.PackId, forceSelectedPackId, StringComparison.Ordinal))
                    ?? SavedContextPackComboBox.Items.OfType<ContextPackSelectionItem>().FirstOrDefault(item => item.IsBlank);

                SavedContextPackComboBox.SelectedItem = selectedItem;
            }
            finally
            {
                _isUpdatingContextPackSelection = false;
            }

            ApplySelectedHostedContextPack();
        }

        private void SavedContextPackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingContextPackSelection || _isInitializing) return;
            ApplySelectedHostedContextPack();
        }

        private void ApplySelectedHostedContextPack()
        {
            if (!IsPremiumAccount())
            {
                SetContextEditorsEditable(true);
                return;
            }

            PersistCurrentLocalDraftIfNeeded();

            var selection = SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem;
            if (selection == null || selection.IsBlank)
            {
                _isEditingSelectedHostedPack = false;
                _lastAppliedSelectionWasLocalDraft = true;
                _localDraftCacheInvalidated = false;
                ContextPackNameTextBox.Text = string.Empty;
                DeleteContextPackButton.IsEnabled = false;
                EditContextPackButton.IsEnabled = false;
                SetContextEditorsEditable(true);
                LoadPackIntoEditors(_contextPackService.GetLocalDraftPack());
                ContextPackStatusText.Text = "Editing the local draft. Click Save Settings to apply this resume and job description.";

                return;
            }

            var pack = _hostedContextPacks.FirstOrDefault(item => string.Equals(item.PackId, selection.PackId, StringComparison.Ordinal));
            if (pack == null)
            {
                return;
            }

            ContextPackNameTextBox.Text = pack.Name;
            _isEditingSelectedHostedPack = false;
            _lastAppliedSelectionWasLocalDraft = false;
            _localDraftCacheInvalidated = false;
            DeleteContextPackButton.IsEnabled = true;
            EditContextPackButton.IsEnabled = true;
            SetContextEditorsEditable(false);
            LoadPackIntoEditors(new ContextPack
            {
                PackId = pack.PackId,
                Name = pack.Name,
                ResumeText = pack.ResumeText,
                JobDescriptionText = pack.JobDescriptionText,
                ResumeSummary = string.Empty,
                JobDescriptionSummary = string.Empty,
                UpdatedAtUtc = pack.UpdatedAtUtc
            });
            ContextPackStatusText.Text = $"Loaded '{pack.Name}' into the editor. Click Save Settings to apply it, or Edit Pack to modify it.";
        }

        private void EditContextPackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPremiumAccount())
            {
                SetContextEditorsEditable(true);
                return;
            }

            var selection = SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem;
            if (selection == null || selection.IsBlank)
            {
                SetContextEditorsEditable(true);
                return;
            }

            _isEditingSelectedHostedPack = true;
            SetContextEditorsEditable(true);
            ResumeBox.Focus();
            ResumeBox.CaretIndex = ResumeBox.Text.Length;
            ContextPackStatusText.Text = $"Editing '{ContextPackNameTextBox.Text}'. Save the pack when you're done.";
        }

        private void SaveContextPackButton_Click(object sender, RoutedEventArgs e)
        {
            SaveHostedContextPack(showSuccessMessage: true);
        }

        private DesktopContextPackDto? SaveHostedContextPack(bool showSuccessMessage)
        {
            if (!IsPremiumAccount())
            {
                if (showSuccessMessage)
                {
                    InvisibleMessageBox.Show("Context Packs are available only for Premium accounts.", "Premium Feature");
                }

                return null;
            }

            var session = _authSessionRepository.Load();
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                if (showSuccessMessage)
                {
                    InvisibleMessageBox.Show("Sign in again to save Premium context packs.", "Authentication Required");
                }

                return null;
            }

            var packName = ContextPackNameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(packName))
            {
                if (showSuccessMessage)
                {
                    InvisibleMessageBox.Show("Enter a context pack name before saving.", "Context Pack");
                }

                return null;
            }

            var resumeText = ResumeBox.Text?.Trim() ?? string.Empty;
            var jobDescriptionText = JobDescriptionBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(resumeText) && string.IsNullOrWhiteSpace(jobDescriptionText))
            {
                if (showSuccessMessage)
                {
                    InvisibleMessageBox.Show(
                        "Add a resume, a job description, or both before saving a context pack.",
                        "Context Pack");
                }

                return null;
            }

            var selectedItem = SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem;
            var selectedPack = selectedItem == null || selectedItem.IsBlank
                ? null
                : _hostedContextPacks.FirstOrDefault(item => string.Equals(item.PackId, selectedItem.PackId, StringComparison.Ordinal));

            var selectedPackId = selectedPack?.PackId ?? string.Empty;

            var hasChanges = selectedPack == null
                || !string.Equals(selectedPack.Name, packName, StringComparison.Ordinal)
                || !string.Equals(selectedPack.ResumeText, resumeText, StringComparison.Ordinal)
                || !string.Equals(selectedPack.JobDescriptionText, jobDescriptionText, StringComparison.Ordinal);

            if (!hasChanges)
            {
                _settings.SelectedHostedContextPackId = selectedPack?.PackId ?? string.Empty;
                SettingsManager.Save(_settings);
                _isEditingSelectedHostedPack = false;
                SetContextEditorsEditable(false);
                ContextPackStatusText.Text = $"No changes to save for '{packName}'.";

                if (showSuccessMessage)
                {
                    InvisibleMessageBox.Show($"No changes to save for '{packName}'.", "Context Pack");
                }

                return selectedPack;
            }

            try
            {
                var savedPack = _hostedAccountClient.SaveContextPack(session.AccessToken, new DesktopContextPackUpsertRequestDto
                {
                    PackId = selectedPackId,
                    Name = packName,
                    ResumeText = resumeText,
                    JobDescriptionText = jobDescriptionText
                });

                _settings.SelectedHostedContextPackId = savedPack.PackId;
                SettingsManager.Save(_settings);
                if (selectedPack == null)
                {
                    SaveEditorsToLocalDraft();
                }

                _isEditingSelectedHostedPack = false;
                SetContextEditorsEditable(false);
                LoadHostedContextPacks(forceSelectedPackId: savedPack.PackId);
                ContextPackStatusText.Text = $"Saved '{savedPack.Name}' to your Premium account. Click Save Settings to apply it to the interview.";

                if (showSuccessMessage)
                {
                    InvisibleMessageBox.Show($"Saved '{savedPack.Name}' to your Premium context packs.", "Context Pack Saved");
                }

                return savedPack;
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted context pack save failed: {ex.Message}");
                if (showSuccessMessage)
                {
                    InvisibleMessageBox.Show($"Could not save context pack:\n\n{ex.Message}", "Context Pack");
                }

                ContextPackStatusText.Text = $"Could not save context pack: {ex.Message}";
                return null;
            }
        }

        private void DeleteContextPackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPremiumAccount()) return;

            var selection = SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem;
            if (selection == null || selection.IsBlank || string.IsNullOrWhiteSpace(selection.PackId))
            {
                return;
            }

            var session = _authSessionRepository.Load();
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                InvisibleMessageBox.Show("Sign in again to delete Premium context packs.", "Authentication Required");
                return;
            }

            try
            {
                _hostedAccountClient.DeleteContextPack(session.AccessToken, selection.PackId);
                _settings.SelectedHostedContextPackId = string.Empty;
                SettingsManager.Save(_settings);
                _isEditingSelectedHostedPack = false;
                SetContextEditorsEditable(true);
                LoadHostedContextPacks();
                ContextPackStatusText.Text = "Context pack deleted.";
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted context pack delete failed: {ex.Message}");
                InvisibleMessageBox.Show($"Could not delete context pack:\n\n{ex.Message}", "Context Pack");
                ContextPackStatusText.Text = $"Could not delete context pack: {ex.Message}";
            }
        }

        private void SaveEditorsToLocalDraft()
        {
            SaveEditorsToLocalDraft(_contextPackService.GetLocalDraftPack());
        }

        private void SaveEditorsToLocalDraft(ContextPack localDraftPack)
        {
            var oldResume = localDraftPack.ResumeText;
            var oldJobDescription = localDraftPack.JobDescriptionText;

            localDraftPack.Name = "Local Context Pack";
            localDraftPack.ResumeText = ResumeBox.Text;
            localDraftPack.JobDescriptionText = JobDescriptionBox.Text;

            if (!string.Equals(oldResume, localDraftPack.ResumeText, StringComparison.Ordinal))
            {
                localDraftPack.ResumeSummary = string.Empty;
                Log.WriteLine("Resume changed - cached summary cleared");
            }

            if (!string.Equals(oldJobDescription, localDraftPack.JobDescriptionText, StringComparison.Ordinal))
            {
                localDraftPack.JobDescriptionSummary = string.Empty;
                Log.WriteLine("Job description changed - cached summary cleared");
            }

            _contextPackService.SaveLocalDraftPack(localDraftPack);
        }

        private void LoadPackIntoEditors(ContextPack selectedPack)
        {
            ResumeBox.Text = selectedPack.ResumeText;
            UpdateResumeWordCount();
            UpdateResumeSummaryStatus(selectedPack);

            JobDescriptionBox.Text = selectedPack.JobDescriptionText;
            UpdateJobDescriptionWordCount();
            UpdateJobDescriptionSummaryStatus(selectedPack);
        }

        private void ClearContextPackEditors()
        {
            LoadPackIntoEditors(new ContextPack());
        }

        private void SetContextEditorsEditable(bool isEditable)
        {
            if (ResumeBox != null)
            {
                ResumeBox.IsReadOnly = !isEditable;
                ResumeBox.Opacity = isEditable ? 1.0 : 0.82;
            }

            if (JobDescriptionBox != null)
            {
                JobDescriptionBox.IsReadOnly = !isEditable;
                JobDescriptionBox.Opacity = isEditable ? 1.0 : 0.82;
            }
        }

        private void LoadApiKeys()
        {
            // ChatGPT
            _chatGPTKeys.Clear();
            if (_settings.ChatGPTApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(_settings.ChatGPTApiKey))
            {
                _settings.ChatGPTApiKeys.Add(_settings.ChatGPTApiKey);
            }
            for (int i = 0; i < _settings.ChatGPTApiKeys.Count; i++)
            {
                _chatGPTKeys.Add(new ApiKeyItem { Index = $"#{i + 1}", Key = _settings.ChatGPTApiKeys[i] });
            }
            ChatGPTKeysList.ItemsSource = _chatGPTKeys;
            
            // Claude
            _claudeKeys.Clear();
            if (_settings.ClaudeApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(_settings.ClaudeApiKey))
            {
                _settings.ClaudeApiKeys.Add(_settings.ClaudeApiKey);
            }
            for (int i = 0; i < _settings.ClaudeApiKeys.Count; i++)
            {
                _claudeKeys.Add(new ApiKeyItem { Index = $"#{i + 1}", Key = _settings.ClaudeApiKeys[i] });
            }
            ClaudeKeysList.ItemsSource = _claudeKeys;
            
            // Mistral
            _mistralKeys.Clear();
            if (_settings.MistralApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(_settings.MistralApiKey))
            {
                _settings.MistralApiKeys.Add(_settings.MistralApiKey);
            }
            for (int i = 0; i < _settings.MistralApiKeys.Count; i++)
            {
                _mistralKeys.Add(new ApiKeyItem { Index = $"#{i + 1}", Key = _settings.MistralApiKeys[i] });
            }
            MistralKeysList.ItemsSource = _mistralKeys;
            
            // Gemini
            _geminiKeys.Clear();
            if (_settings.GeminiApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
            {
                _settings.GeminiApiKeys.Add(_settings.GeminiApiKey);
            }
            for (int i = 0; i < _settings.GeminiApiKeys.Count; i++)
            {
                _geminiKeys.Add(new ApiKeyItem { Index = $"#{i + 1}", Key = _settings.GeminiApiKeys[i] });
            }
            GeminiKeysList.ItemsSource = _geminiKeys;
            
            // Groq
            _groqKeys.Clear();
            if (_settings.GroqApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(_settings.GroqApiKey))
            {
                _settings.GroqApiKeys.Add(_settings.GroqApiKey);
            }
            for (int i = 0; i < _settings.GroqApiKeys.Count; i++)
            {
                _groqKeys.Add(new ApiKeyItem { Index = $"#{i + 1}", Key = _settings.GroqApiKeys[i] });
            }
            GroqKeysList.ItemsSource = _groqKeys;

            _nvidiaKeys.Clear();
            if (_settings.NvidiaApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(_settings.NvidiaApiKey))
            {
                _settings.NvidiaApiKeys.Add(_settings.NvidiaApiKey);
            }
            for (int i = 0; i < _settings.NvidiaApiKeys.Count; i++)
            {
                _nvidiaKeys.Add(new ApiKeyItem { Index = $"#{i + 1}", Key = _settings.NvidiaApiKeys[i] });
            }
            NvidiaKeysList.ItemsSource = _nvidiaKeys;
        }

        // ═══════════════════════════════════════════════════════════════
        // API KEY MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        private void AddChatGPTKey_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAddProviderKey(_chatGPTKeys, "ChatGPT")) return;
            _chatGPTKeys.Add(new ApiKeyItem { Index = $"#{_chatGPTKeys.Count + 1}", Key = "" });
        }

        private void RemoveChatGPTKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ApiKeyItem item)
            {
                _chatGPTKeys.Remove(item);
                ReindexKeys(_chatGPTKeys);
            }
        }

        private void AddClaudeKey_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAddProviderKey(_claudeKeys, "Claude")) return;
            _claudeKeys.Add(new ApiKeyItem { Index = $"#{_claudeKeys.Count + 1}", Key = "" });
        }

        private void RemoveClaudeKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ApiKeyItem item)
            {
                _claudeKeys.Remove(item);
                ReindexKeys(_claudeKeys);
            }
        }

        private void AddMistralKey_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAddProviderKey(_mistralKeys, "Mistral")) return;
            _mistralKeys.Add(new ApiKeyItem { Index = $"#{_mistralKeys.Count + 1}", Key = "" });
        }

        private void RemoveMistralKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ApiKeyItem item)
            {
                _mistralKeys.Remove(item);
                ReindexKeys(_mistralKeys);
            }
        }

        private void AddGeminiKey_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAddProviderKey(_geminiKeys, "Gemini")) return;
            _geminiKeys.Add(new ApiKeyItem { Index = $"#{_geminiKeys.Count + 1}", Key = "" });
        }

        private void RemoveGeminiKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ApiKeyItem item)
            {
                _geminiKeys.Remove(item);
                ReindexKeys(_geminiKeys);
            }
        }

        private void AddGroqKey_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAddProviderKey(_groqKeys, "Groq")) return;
            _groqKeys.Add(new ApiKeyItem { Index = $"#{_groqKeys.Count + 1}", Key = "" });
        }

        private void RemoveGroqKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ApiKeyItem item)
            {
                _groqKeys.Remove(item);
                ReindexKeys(_groqKeys);
            }
        }

        private void AddNvidiaKey_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAddProviderKey(_nvidiaKeys, "NVIDIA")) return;
            _nvidiaKeys.Add(new ApiKeyItem { Index = $"#{_nvidiaKeys.Count + 1}", Key = "" });
        }

        private void RemoveNvidiaKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ApiKeyItem item)
            {
                _nvidiaKeys.Remove(item);
                ReindexKeys(_nvidiaKeys);
            }
        }

        private void ReindexKeys(ObservableCollection<ApiKeyItem> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                keys[i].Index = $"#{i + 1}";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // EXISTING METHODS (unchanged)
        // ═══════════════════════════════════════════════════════════════

        private void AIProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateManagedModelChoices();
            UpdatePanelVisibility();
        }

        private void JobDescriptionBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateJobDescriptionWordCount();
            InvalidateDefaultDraftCacheIfNeeded();
        }

        private void UpdateJobDescriptionWordCount()
        {
            if (JobDescriptionBox == null || JobDescriptionWordCount == null) return;
            
            try
            {
                var text = JobDescriptionBox.Text;
                var wordCount = string.IsNullOrWhiteSpace(text) ? 0 : 
                    text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                
                JobDescriptionWordCount.Text = $"Words: {wordCount} / 350";
                
                if (wordCount > 350)
                {
                    JobDescriptionWordCount.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 100, 100));
                }
                else
                {
                    JobDescriptionWordCount.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(170, 170, 170));
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error updating JD word count: {ex.Message}");
            }
        }

        private void UpdateJobDescriptionSummaryStatus(ContextPack selectedPack)
        {
            if (JobDescriptionSummaryStatus == null) return;
            
            try
            {
                if (!string.IsNullOrWhiteSpace(selectedPack.JobDescriptionSummary))
                {
                    JobDescriptionSummaryStatus.Text = "✓ Cached summary available";
                }
                else if (!string.IsNullOrWhiteSpace(selectedPack.JobDescriptionText))
                {
                    JobDescriptionSummaryStatus.Text = "⚠ Will be summarized on first use";
                }
                else
                {
                    JobDescriptionSummaryStatus.Text = "";
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error updating JD status: {ex.Message}");
            }
        }

        private void UpdatePanelVisibility()
        {
            if (AIProviderComboBox.SelectedItem == null) return;

            var selected = AIProviderComboBox.SelectedItem as string;
            ProviderSelectionSection.Visibility = IsPremiumOnlyAccount() ? Visibility.Collapsed : Visibility.Visible;
            PremiumManagedModelRow.Visibility = Visibility.Collapsed;
            if (IsPremiumOnlyAccount())
            {
                ChatGPTPanel.Visibility = Visibility.Collapsed;
                ClaudePanel.Visibility = Visibility.Collapsed;
                MistralPanel.Visibility = Visibility.Collapsed;
                GeminiPanel.Visibility = Visibility.Collapsed;
                GroqPanel.Visibility = Visibility.Collapsed;
                NvidiaPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ChatGPTPanel.Visibility = selected == "ChatGPT" ? Visibility.Visible : Visibility.Collapsed;
            ClaudePanel.Visibility = selected == "Claude" ? Visibility.Visible : Visibility.Collapsed;
            MistralPanel.Visibility = selected == "Mistral" ? Visibility.Visible : Visibility.Collapsed;
            GeminiPanel.Visibility = selected == "Gemini" ? Visibility.Visible : Visibility.Collapsed;
            GroqPanel.Visibility = selected == "Groq" ? Visibility.Visible : Visibility.Collapsed;
            NvidiaPanel.Visibility = selected == "NVIDIA" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PopulateProviderChoices()
        {
            AIProviderComboBox.Items.Clear();

            IEnumerable<string> providers = IsPremiumOnlyAccount()
                ? (_settings.PremiumConfiguredProviders?.Count > 0
                    ? _settings.PremiumConfiguredProviders
                    : (_settings.ManagedAiCatalogCache?.Providers ?? new List<ManagedAiProviderOptionDto>())
                        .Select(item => item.ProviderId))
                : AIModelRegistry.GetAllProviders();

            foreach (var provider in providers.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AIProviderComboBox.Items.Add(provider);
            }

            if (AIProviderComboBox.Items.Count == 0 && !string.IsNullOrWhiteSpace(_settings.SelectedAI))
            {
                AIProviderComboBox.Items.Add(_settings.SelectedAI);
            }
        }

        private void PopulateManagedModelChoices()
        {
            ManagedModelComboBox.Items.Clear();
            if (!IsPremiumOnlyAccount())
            {
                return;
            }

            var selectedProvider = AIProviderComboBox.SelectedItem as string ?? _settings.SelectedAI;
            var provider = ProviderModelCatalogCache.GetProvider(_settings, selectedProvider);
            if (provider == null)
            {
                return;
            }

            foreach (var model in provider.Models)
            {
                ManagedModelComboBox.Items.Add(model.ModelId);
            }

            var selectedModel = AIModelRegistry.GetCurrentModelForProvider(_settings, provider.ProviderId);
            ManagedModelComboBox.SelectedItem = provider.Models.Any(item => item.ModelId == selectedModel)
                ? selectedModel
                : provider.Models.FirstOrDefault()?.ModelId;
        }

        private void PopulateByoModelChoices()
        {
            RebindModelCombo(ChatGPTModelBox, ProviderModelCatalogCache.GetModelIds(_settings, AIModelRegistry.Providers.ChatGPT));
            RebindModelCombo(ClaudeModelBox, ProviderModelCatalogCache.GetModelIds(_settings, AIModelRegistry.Providers.Claude));
            RebindModelCombo(MistralModelBox, ProviderModelCatalogCache.GetModelIds(_settings, AIModelRegistry.Providers.Mistral));
            RebindModelCombo(GeminiModelBox, ProviderModelCatalogCache.GetModelIds(_settings, AIModelRegistry.Providers.Gemini));
            RebindModelCombo(GroqModelBox, ProviderModelCatalogCache.GetModelIds(_settings, AIModelRegistry.Providers.Groq));
            RebindModelCombo(NvidiaModelBox, ProviderModelCatalogCache.GetModelIds(_settings, AIModelRegistry.Providers.Nvidia));
        }

        private static void RebindModelCombo(ComboBox comboBox, IEnumerable<string> models)
        {
            comboBox.Items.Clear();
            foreach (var model in models.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                comboBox.Items.Add(model);
            }
        }

        private void UseFakeCursorCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateFakeCursorPanelVisibility();
        }

        private void UpdateFakeCursorPanelVisibility()
        {
            if (FakeCursorSizePanel == null) return;
            
            if (UseFakeCursorCheckBox.IsChecked == true)
            {
                FakeCursorSizePanel.Visibility = Visibility.Visible;
            }
            else
            {
                FakeCursorSizePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void SettingsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            if (e.OriginalSource is DependencyObject source
                && FindAncestor<TextBoxBase>(source) != null)
            {
                return;
            }

            const double scrollStep = 42d;
            var delta = e.Delta > 0 ? -scrollStep : scrollStep;
            var nextOffset = Math.Max(0d, Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + delta));
            scrollViewer.ScrollToVerticalOffset(nextOffset);
            e.Handled = true;
        }

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T match)
                {
                    return match;
                }

                if (source is System.Windows.Media.Visual || source is System.Windows.Media.Media3D.Visual3D)
                {
                    source = System.Windows.Media.VisualTreeHelper.GetParent(source);
                    continue;
                }

                if (source is FrameworkContentElement frameworkContentElement)
                {
                    source = frameworkContentElement.Parent;
                    continue;
                }

                if (source is ContentElement contentElement)
                {
                    source = ContentOperations.GetParent(contentElement)
                        ?? (contentElement as FrameworkContentElement)?.Parent;
                    continue;
                }

                break;
            }

            return null;
        }

        private void FakeCursorSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSlider || _isInitializing) return;
            if (FakeCursorSizeTextBox == null) return;

            try
            {
                _isUpdatingSlider = true;
                
                FakeCursorSizeTextBox.Text = $"{FakeCursorSizeSlider.Value:F0}";
                
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                mainWindow?.UpdateFakeCursorPreviewSize(FakeCursorSizeSlider.Value / 100.0);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error in slider value changed: {ex.Message}");
            }
            finally
            {
                _isUpdatingSlider = false;
            }
        }

        private void FakeCursorSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingSlider || _isInitializing) return;
            if (FakeCursorSizeSlider == null) return;

            try
            {
                if (double.TryParse(FakeCursorSizeTextBox.Text, out double value))
                {
                    value = Math.Max(50, Math.Min(200, value));
                    
                    _isUpdatingSlider = true;
                    
                    FakeCursorSizeSlider.Value = value;
                    
                    var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                    mainWindow?.UpdateFakeCursorPreviewSize(value / 100.0);
                    
                    _isUpdatingSlider = false;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error in textbox changed: {ex.Message}");
                _isUpdatingSlider = false;
            }
        }

        private void FakeCursorSizeTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                Regex regex = new Regex("[^0-9]+");
                e.Handled = regex.IsMatch(e.Text);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error in text input validation: {ex.Message}");
            }
        }

        private void FakeCursorSizeSlider_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isInitializing) return;
            
            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                mainWindow?.ShowFakeCursorPreview();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error showing preview: {ex.Message}");
            }
        }

        private void FakeCursorSizeSlider_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isInitializing) return;
            
            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                mainWindow?.HideFakeCursorPreview();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error hiding preview: {ex.Message}");
            }
        }

        private void ResumeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateResumeWordCount();
            InvalidateDefaultDraftCacheIfNeeded();
        }

        private void UpdateResumeWordCount()
        {
            if (ResumeBox == null || ResumeWordCount == null) return;
            
            try
            {
                var text = ResumeBox.Text;
                var wordCount = string.IsNullOrWhiteSpace(text) ? 0 : 
                    text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                
                ResumeWordCount.Text = $"Words: {wordCount} / 1000";
                
                if (wordCount > 1000)
                {
                    ResumeWordCount.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 100, 100));
                }
                else
                {
                    ResumeWordCount.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(170, 170, 170));
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error updating word count: {ex.Message}");
            }
        }

        private void UpdateResumeSummaryStatus(ContextPack selectedPack)
        {
            if (ResumeSummaryStatus == null) return;
            
            try
            {
                if (!string.IsNullOrWhiteSpace(selectedPack.ResumeSummary))
                {
                    ResumeSummaryStatus.Text = "✓ Cached summary available";
                }
                else if (!string.IsNullOrWhiteSpace(selectedPack.ResumeText))
                {
                    ResumeSummaryStatus.Text = "⚠ Will be summarized on first use";
                }
                else
                {
                    ResumeSummaryStatus.Text = "";
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error updating resume status: {ex.Message}");
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error opening link: {ex.Message}");
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var previousAppliedPack = _contextPackService.GetSelectedPack();
                var previousLocalDraftApplied = _contextPackService.IsLocalDraftApplied();
                _settings.SelectedAI = AIProviderComboBox.SelectedItem as string ?? "ChatGPT";
                
                if (IsPremiumOnlyAccount())
                {
                    _settings.ChatGPTApiKeys = new System.Collections.Generic.List<string>();
                    _settings.ClaudeApiKeys = new System.Collections.Generic.List<string>();
                    _settings.MistralApiKeys = new System.Collections.Generic.List<string>();
                    _settings.GeminiApiKeys = new System.Collections.Generic.List<string>();
                    _settings.GroqApiKeys = new System.Collections.Generic.List<string>();
                    _settings.NvidiaApiKeys = new System.Collections.Generic.List<string>();
                }
                else
                {
                    _settings.ChatGPTApiKeys = _chatGPTKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.ClaudeApiKeys = _claudeKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.MistralApiKeys = _mistralKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.GeminiApiKeys = _geminiKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.GroqApiKeys = _groqKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.NvidiaApiKeys = _nvidiaKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                }

                ValidateByoProviderLimits();
                
                // Detailed logging
                Log.WriteLine("═══════════════════════════════════════════════════════");
                Log.WriteLine("SAVING API KEYS:");
                Log.WriteLine($"  ChatGPT: {_settings.ChatGPTApiKeys.Count} keys");
                Log.WriteLine($"  Claude: {_settings.ClaudeApiKeys.Count} keys");
                Log.WriteLine($"  Mistral: {_settings.MistralApiKeys.Count} keys");
                Log.WriteLine($"  Gemini: {_settings.GeminiApiKeys.Count} keys");
                Log.WriteLine($"  Groq: {_settings.GroqApiKeys.Count} keys");
                Log.WriteLine($"  NVIDIA: {_settings.NvidiaApiKeys.Count} keys");
                Log.WriteLine("═══════════════════════════════════════════════════════");
                
                // Save legacy single keys (use first key if available)
                _settings.ChatGPTApiKey = _settings.ChatGPTApiKeys.FirstOrDefault() ?? "";
                _settings.ClaudeApiKey = _settings.ClaudeApiKeys.FirstOrDefault() ?? "";
                _settings.MistralApiKey = _settings.MistralApiKeys.FirstOrDefault() ?? "";
                _settings.GeminiApiKey = _settings.GeminiApiKeys.FirstOrDefault() ?? "";
                _settings.GroqApiKey = _settings.GroqApiKeys.FirstOrDefault() ?? "";
                _settings.NvidiaApiKey = _settings.NvidiaApiKeys.FirstOrDefault() ?? "";
                
                // Save models
                _settings.ChatGPTModel = ChatGPTModelBox.SelectedItem as string ?? "gpt-4";
                _settings.ClaudeModel = ClaudeModelBox.SelectedItem as string ?? "claude-3-sonnet-20240229";
                _settings.MistralModel = MistralModelBox.SelectedItem as string ?? "mistral-large-latest";
                _settings.GeminiModel = GeminiModelBox.SelectedItem as string ?? "gemini-2.5-flash";
                _settings.GroqModel = GroqModelBox.SelectedItem as string ?? "llama-3.3-70b-versatile";
                _settings.NvidiaModel = NvidiaModelBox.SelectedItem as string ?? _settings.NvidiaModel;

                if (IsPremiumOnlyAccount())
                {
                    AIModelRegistry.SetModelForProvider(
                        _settings,
                        _settings.SelectedAI,
                        ManagedModelComboBox.SelectedItem as string ?? AIModelRegistry.GetCurrentModelForProvider(_settings, _settings.SelectedAI));
                }

                // Save rotation settings
                _settings.AutoSwitchKeysOnError = AutoSwitchKeysCheckBox.IsChecked == true;
                _settings.AutoSwitchModelsOnError = AutoSwitchModelsCheckBox.IsChecked == true;
                _settings.AllowFreeTrialSessionExtension = IsFreeTrialAccount() && SessionContinuationCheckBox.IsChecked == true;
                _settings.AllowByoSessionExtension = !IsFreeTrialAccount() && SessionContinuationCheckBox.IsChecked == true;

                _settings.VoiceInputEnabled = VoiceInputCheckBox.IsChecked == true;
                _settings.AutoSendAfterVoiceStopEnabled = AutoSendAfterVoiceStopCheckBox.IsChecked == true;
                
                _settings.UseFakeCursor = UseFakeCursorCheckBox.IsChecked == true;
                
                if (double.TryParse(FakeCursorSizeTextBox.Text, out double percentage))
                {
                    _settings.FakeCursorSize = Math.Max(0.5, Math.Min(2.0, percentage / 100.0));
                }
                else
                {
                    _settings.FakeCursorSize = 1.0;
                }
                
                _settings.InterviewPromptType = InterviewTypeComboBox.SelectedItem as string
                    ?? InterviewPromptRegistry.InterviewTypes.Technical;
                _settings.SelectedHostedContextPackId =
                    (SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem)?.IsBlank == false
                        ? (SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem)?.PackId ?? string.Empty
                        : string.Empty;
                _settings.AutoPauseOnInactivityEnabled = AutoPauseInactivityCheckBox.IsChecked == true;
                if (int.TryParse(AutoPauseMinutesTextBox.Text, out var autoPauseMinutes))
                {
                    if (autoPauseMinutes < 10)
                    {
                        InvisibleMessageBox.Show(
                            "Session auto pause must be at least 10 minutes.",
                            "Invalid Auto Pause");
                        return;
                    }

                    _settings.AutoPauseOnInactivityMinutes = autoPauseMinutes;
                }
                else
                {
                    _settings.AutoPauseOnInactivityMinutes = 10;
                }

                // debug mode:
                _settings.DebugModeEnabled = HasByoEntitlement() && DebugModeCheckBox.IsChecked == true;
                _settings.DebugErrorSimulation = _settings.DebugModeEnabled
                    ? (DebugErrorTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "None"
                    : "None";

                if (_settings.DebugModeEnabled)
                {
                    Log.WriteLine($"✓ Debug mode enabled: {_settings.DebugErrorSimulation}");
                }
                
                ByoProviderModelCatalogService.RefreshStaleCatalogs(_settings);
                SettingsManager.Save(_settings);

                var selectedHostedPack = (SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem)?.IsBlank == false;
                if (!IsPremiumAccount() || !selectedHostedPack)
                {
                    var localDraftPack = _contextPackService.GetLocalDraftPack();
                    SaveEditorsToLocalDraft(localDraftPack);
                    localDraftPack.ResumeSummary = string.Empty;
                    localDraftPack.JobDescriptionSummary = string.Empty;
                    _contextPackService.SaveLocalDraftPack(localDraftPack);
                    _contextPackService.SaveSelectedPack(CloneForApply(localDraftPack), preserveCachedSummaries: false);
                }
                else
                {
                    DesktopContextPackDto? selectedHostedContextPack;
                    if (HasPendingSelectedHostedPackChanges())
                    {
                        selectedHostedContextPack = SaveHostedContextPack(showSuccessMessage: false);
                        if (selectedHostedContextPack == null)
                        {
                            InvisibleMessageBox.Show("Could not save the selected Premium context pack.", "Context Pack");
                            return;
                        }
                    }
                    else
                    {
                        var selection = SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem;
                        selectedHostedContextPack = _hostedContextPacks.FirstOrDefault(
                            item => string.Equals(item.PackId, selection?.PackId, StringComparison.Ordinal));
                    }

                    if (selectedHostedContextPack != null)
                    {
                        _contextPackService.SaveSelectedPack(new ContextPack
                        {
                            PackId = selectedHostedContextPack.PackId,
                            Name = selectedHostedContextPack.Name,
                            ResumeText = selectedHostedContextPack.ResumeText,
                            JobDescriptionText = selectedHostedContextPack.JobDescriptionText,
                            UpdatedAtUtc = selectedHostedContextPack.UpdatedAtUtc
                        }, preserveCachedSummaries: false);
                    }
                }

                Log.WriteLine($"✓ Settings saved:");
                Log.WriteLine($"  ChatGPT keys: {_settings.ChatGPTApiKeys.Count}");
                Log.WriteLine($"  Claude keys: {_settings.ClaudeApiKeys.Count}");
                Log.WriteLine($"  Mistral keys: {_settings.MistralApiKeys.Count}");
                Log.WriteLine($"  Gemini keys: {_settings.GeminiApiKeys.Count}");
                Log.WriteLine($"  Groq keys: {_settings.GroqApiKeys.Count}");
                Log.WriteLine($"  NVIDIA keys: {_settings.NvidiaApiKeys.Count}");
                Log.WriteLine($"  Auto-switch keys: {_settings.AutoSwitchKeysOnError}");
                Log.WriteLine($"  Auto-switch models: {_settings.AutoSwitchModelsOnError}");

                var appliedPack = _contextPackService.GetSelectedPack();
                var contextResetRequired = previousLocalDraftApplied != _contextPackService.IsLocalDraftApplied()
                    || ShouldResetConversationForAppliedContextChange(previousAppliedPack, appliedPack)
                    || _localDraftCacheInvalidated;

                SettingsClosed?.Invoke(this, new SettingsCloseResult
                {
                    Saved = true,
                    ContextResetRequired = contextResetRequired
                });
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error saving settings: {ex.Message}");
                InvisibleMessageBox.Show($"Error saving settings:\n\n{ex.Message}", "Error");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsClosed?.Invoke(this, new SettingsCloseResult { Saved = false });
        }

        private void PersistCurrentLocalDraftIfNeeded()
        {
            if (_isInitializing || !_lastAppliedSelectionWasLocalDraft)
            {
                return;
            }

            SaveEditorsToLocalDraft();
        }

        private static ContextPack CloneForApply(ContextPack source)
        {
            return new ContextPack
            {
                PackId = source.PackId,
                Name = source.Name,
                ResumeText = source.ResumeText,
                ResumeSummary = source.ResumeSummary,
                JobDescriptionText = source.JobDescriptionText,
                JobDescriptionSummary = source.JobDescriptionSummary,
                UpdatedAtUtc = source.UpdatedAtUtc
            };
        }

        private DesktopContextPackDto? GetSelectedHostedPack()
        {
            var selection = SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem;
            if (selection == null || selection.IsBlank)
            {
                return null;
            }

            return _hostedContextPacks.FirstOrDefault(
                item => string.Equals(item.PackId, selection.PackId, StringComparison.Ordinal));
        }

        private bool HasPendingSelectedHostedPackChanges()
        {
            var selectedPack = GetSelectedHostedPack();
            if (selectedPack == null)
            {
                return false;
            }

            return !string.Equals(selectedPack.Name, ContextPackNameTextBox.Text?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(selectedPack.ResumeText, ResumeBox.Text?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(selectedPack.JobDescriptionText, JobDescriptionBox.Text?.Trim() ?? string.Empty, StringComparison.Ordinal);
        }

        private static bool ShouldResetConversationForAppliedContextChange(
            ContextPack previousAppliedPack,
            ContextPack appliedPack)
        {
            return !HasSameAppliedContent(previousAppliedPack, appliedPack);
        }

        private static bool HasSameAppliedContent(ContextPack left, ContextPack right)
        {
            return string.Equals(left.ResumeText ?? string.Empty, right.ResumeText ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(left.JobDescriptionText ?? string.Empty, right.JobDescriptionText ?? string.Empty, StringComparison.Ordinal);
        }

        private void InvalidateDefaultDraftCacheIfNeeded()
        {
            if (_isInitializing || !IsEditingLocalDraftSelection())
            {
                return;
            }

            var localDraftPack = _contextPackService.GetLocalDraftPack();
            var resumeChanged = !string.Equals(localDraftPack.ResumeText ?? string.Empty, ResumeBox.Text ?? string.Empty, StringComparison.Ordinal);
            var jobDescriptionChanged = !string.Equals(localDraftPack.JobDescriptionText ?? string.Empty, JobDescriptionBox.Text ?? string.Empty, StringComparison.Ordinal);

            if (!resumeChanged && !jobDescriptionChanged)
            {
                return;
            }

            SettingsManager.ClearConversationCache();
            _localDraftCacheInvalidated = true;
            ContextPackStatusText.Text = "Local draft changed. Cached conversation cleared. Click Save Settings to rebuild the summary with your latest resume and job description.";
        }

        private bool IsEditingLocalDraftSelection()
        {
            if (!IsPremiumAccount())
            {
                return true;
            }

            var selection = SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem;
            return selection == null || selection.IsBlank;
        }
        private void ChatGPTModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            var selectedModel = ChatGPTModelBox.SelectedItem as string;
            Log.WriteLine($"User selected ChatGPT model: {selectedModel}");
        }

        private void ClaudeModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            var selectedModel = ClaudeModelBox.SelectedItem as string;
            Log.WriteLine($"User selected Claude model: {selectedModel}");
        }

        private void MistralModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            var selectedModel = MistralModelBox.SelectedItem as string;
            Log.WriteLine($"User selected Mistral model: {selectedModel}");
        }

        private void GeminiModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            var selectedModel = GeminiModelBox.SelectedItem as string;
            Log.WriteLine($"User selected Gemini model: {selectedModel}");
        }

        private void GroqModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            var selectedModel = GroqModelBox.SelectedItem as string;
            Log.WriteLine($"User selected Groq model: {selectedModel}");
        }

        private void ApplyAccountTierRestrictions()
        {
            var isPremium = HasPremiumManagedEntitlement();
            var isFreeTrial = IsFreeTrialAccount();
            var isByo = HasByoEntitlement();
            var isPremiumOnly = IsPremiumOnlyAccount();

            PremiumManagedNotice.Visibility = isPremium ? Visibility.Visible : Visibility.Collapsed;
            FreeTrialNotice.Visibility = isFreeTrial ? Visibility.Visible : Visibility.Collapsed;
            SessionContinuationNotice.Visibility = (isFreeTrial || (!isFreeTrial && (isPremium || isByo)))
                ? Visibility.Visible
                : Visibility.Collapsed;
            KnowledgeBaseStatusNotice.Visibility = IsPremiumAccount() ? Visibility.Visible : Visibility.Collapsed;
            ContextPackSection.Visibility = IsPremiumAccount() ? Visibility.Visible : Visibility.Collapsed;
            ByoConfigurationSection.Visibility = (isByo || isPremium) ? Visibility.Visible : Visibility.Collapsed;
            DebugModeSection.Visibility = isByo ? Visibility.Visible : Visibility.Collapsed;

            if (!isByo)
            {
                _settings.DebugModeEnabled = false;
                _settings.DebugErrorSimulation = "None";
                if (DebugModeCheckBox != null)
                {
                    DebugModeCheckBox.IsChecked = false;
                }

                if (DebugErrorTypeComboBox != null)
                {
                    DebugErrorTypeComboBox.SelectedIndex = 0;
                }
            }

            if (isPremiumOnly)
            {
                PremiumManagedNoticeTitle.Text = "Premium Managed AI";
                PremiumManagedNoticeBody.Text =
                    "Premium-only accounts use Phantom-managed provider keys. Only providers with configured managed API keys are listed here, and model capabilities come from the backend catalog.";
            }
            else if (isPremium && isByo)
            {
                PremiumManagedNoticeTitle.Text = "Premium With BYO Fallback";
                PremiumManagedNoticeBody.Text =
                    "Premium credits use Phantom-managed provider keys first. BYO provider keys remain available here for fallback and for BYO-only providers.";
            }

            if (isFreeTrial)
            {
                SessionContinuationTitle.Text = "Free Trial Extension";
                SessionContinuationDescription.Text =
                    "Phantom can stop the current interview at the first 15-minute demo block, or continue into the second demo block only when you explicitly allow it.";
                SessionContinuationCheckBox.Content =
                    "Allow this interview to continue into the next free-trial 15-minute block";
                SessionContinuationCheckBox.IsChecked = _settings.AllowFreeTrialSessionExtension;
            }
            else
            {
                SessionContinuationTitle.Text = "Paid Session Extension";
                SessionContinuationDescription.Text =
                    "Premium is consumed first. If Premium is depleted and BYO is available, Phantom falls back to BYO. This setting only matters if the interview would continue after all available paid credits are exhausted.";
                SessionContinuationCheckBox.Content =
                    "Allow this interview to continue after available paid credits are exhausted";
                SessionContinuationCheckBox.IsChecked = _settings.AllowByoSessionExtension;
            }

            UpdateKnowledgeBaseStatusNotice();
            SaveContextPackButton.IsEnabled = IsPremiumAccount();
            EditContextPackButton.IsEnabled = IsPremiumAccount()
                && (SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem)?.IsBlank == false;
            DeleteContextPackButton.IsEnabled = IsPremiumAccount()
                && (SavedContextPackComboBox.SelectedItem as ContextPackSelectionItem)?.IsBlank == false;

            UpdatePanelVisibility();
        }

        private void UpdateKnowledgeBaseStatusNotice()
        {
            if (KnowledgeBaseStatusBody == null || KnowledgeBaseStatusTitle == null)
            {
                return;
            }

            var hostedKnowledgeBase = _accountSnapshot?.HostedKnowledgeBase;
            if (!IsPremiumAccount())
            {
                KnowledgeBaseStatusNotice.Visibility = Visibility.Collapsed;
                return;
            }

            KnowledgeBaseStatusNotice.Visibility = Visibility.Visible;
            if (hostedKnowledgeBase == null || string.IsNullOrWhiteSpace(hostedKnowledgeBase.KnowledgeBaseId))
            {
                KnowledgeBaseStatusTitle.Text = "Premium Knowledge Base";
                KnowledgeBaseStatusBody.Text =
                    "No hosted knowledge base is linked to this Premium account yet. Create one from the website dashboard to sync interview context across devices.";
                return;
            }

            KnowledgeBaseStatusTitle.Text = hostedKnowledgeBase.Name;
            KnowledgeBaseStatusBody.Text = hostedKnowledgeBase.CanUseInInterview
                ? $"Ready across devices. {hostedKnowledgeBase.DocumentCount} documents and {hostedKnowledgeBase.ChunkCount} retrieval chunks are linked for interview use."
                : string.IsNullOrWhiteSpace(hostedKnowledgeBase.BlockedReason)
                    ? $"Linked, but not ready yet. Status: {hostedKnowledgeBase.Status}."
                    : $"{hostedKnowledgeBase.BlockedReason} Current status: {hostedKnowledgeBase.Status}.";
        }

        private bool IsPremiumAccount()
        {
            return string.Equals(_accountSnapshot?.AccessTier, "premium", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPremiumOnlyAccount()
        {
            return HasPremiumManagedEntitlement() && !HasByoEntitlement();
        }

        private bool HasPremiumManagedEntitlement()
        {
            return !IsFreeTrialAccount()
                && (((_accountSnapshot?.PremiumAvailableCredits ?? 0m) > 0m)
                    || string.Equals(_accountSnapshot?.AccessTier, "premium", StringComparison.OrdinalIgnoreCase));
        }

        private bool IsFreeTrialAccount()
        {
            return string.Equals(_accountSnapshot?.AccessTier, "free", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsByoAccount()
        {
            return string.Equals(_accountSnapshot?.AccessTier, "pro_byo", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasByoEntitlement()
        {
            return !IsFreeTrialAccount()
                && (((_accountSnapshot?.ProAvailableCredits ?? 0m) > 0m)
                    || string.Equals(_accountSnapshot?.AccessTier, "pro_byo", StringComparison.OrdinalIgnoreCase));
        }

        private bool CanAddProviderKey(ObservableCollection<ApiKeyItem> providerKeys, string providerName)
        {
            if (!HasByoEntitlement())
            {
                InvisibleMessageBox.Show("Upgrade to Pro BYO to configure provider keys and models.", "Free Trial");
                return false;
            }

            if (providerKeys.Count >= MaxKeysPerProvider)
            {
                InvisibleMessageBox.Show(
                    $"{providerName} is limited to {MaxKeysPerProvider} API keys for BYO accounts.",
                    "Provider Key Limit");
                return false;
            }

            var activeProviders = CountConfiguredProviders(providerKeys);
            if (providerKeys.Count == 0 && activeProviders >= MaxProvidersForByo)
            {
                InvisibleMessageBox.Show(
                    $"BYO accounts can configure at most {MaxProvidersForByo} providers.",
                    "Provider Limit");
                return false;
            }

            return true;
        }

        private int CountConfiguredProviders(ObservableCollection<ApiKeyItem>? pendingProvider = null)
        {
            var count = 0;
            if (_chatGPTKeys.Any(k => !string.IsNullOrWhiteSpace(k.Key)) || pendingProvider == _chatGPTKeys) count++;
            if (_claudeKeys.Any(k => !string.IsNullOrWhiteSpace(k.Key)) || pendingProvider == _claudeKeys) count++;
            if (_mistralKeys.Any(k => !string.IsNullOrWhiteSpace(k.Key)) || pendingProvider == _mistralKeys) count++;
            if (_geminiKeys.Any(k => !string.IsNullOrWhiteSpace(k.Key)) || pendingProvider == _geminiKeys) count++;
            if (_groqKeys.Any(k => !string.IsNullOrWhiteSpace(k.Key)) || pendingProvider == _groqKeys) count++;
            if (_nvidiaKeys.Any(k => !string.IsNullOrWhiteSpace(k.Key)) || pendingProvider == _nvidiaKeys) count++;
            return count;
        }

        private void ValidateByoProviderLimits()
        {
            if (!HasByoEntitlement())
            {
                return;
            }

            var providerLists = new[]
            {
                new { Name = "ChatGPT", Keys = _settings.ChatGPTApiKeys },
                new { Name = "Claude", Keys = _settings.ClaudeApiKeys },
                new { Name = "Mistral", Keys = _settings.MistralApiKeys },
                new { Name = "Gemini", Keys = _settings.GeminiApiKeys },
                new { Name = "Groq", Keys = _settings.GroqApiKeys },
                new { Name = "NVIDIA", Keys = _settings.NvidiaApiKeys }
            };

            var configuredProviders = providerLists.Count(item => item.Keys.Count > 0);
            if (configuredProviders > MaxProvidersForByo)
            {
                throw new InvalidOperationException($"BYO accounts can configure at most {MaxProvidersForByo} providers.");
            }

            var oversizedProvider = providerLists.FirstOrDefault(item => item.Keys.Count > MaxKeysPerProvider);
            if (oversizedProvider != null)
            {
                throw new InvalidOperationException(
                    $"{oversizedProvider.Name} can store at most {MaxKeysPerProvider} API keys for BYO accounts.");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER CLASSES
    // ═══════════════════════════════════════════════════════════════

    public class ApiKeyItem : INotifyPropertyChanged
    {
        private string _index = "";
        private string _key = "";
        
        public string Index 
        { 
            get => _index;
            set { _index = value; OnPropertyChanged(nameof(Index)); }
        }
        
        public string Key 
        { 
            get => _key;
            set { _key = value; OnPropertyChanged(nameof(Key)); }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        
    }

    public sealed class SettingsCloseResult : EventArgs
    {
        public bool Saved { get; init; }
        public bool ContextResetRequired { get; init; }
    }

    public sealed class ContextPackSelectionItem
    {
        public string PackId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsBlank { get; set; }

        public static ContextPackSelectionItem CreateBlank()
        {
            return new ContextPackSelectionItem
            {
                PackId = string.Empty,
                DisplayName = "(No saved context pack selected)",
                IsBlank = true
            };
        }
    }
}
