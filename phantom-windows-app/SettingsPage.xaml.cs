using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using SecureOverlay.Application.Context;
using SecureOverlay.Services;
using SecureOverlay.Helpers;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Context;
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
        private bool _isUpdatingSlider = false;
        private bool _isInitializing = true;

        // API Key collections
        private ObservableCollection<ApiKeyItem> _chatGPTKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _claudeKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _mistralKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _geminiKeys = new ObservableCollection<ApiKeyItem>();
        private ObservableCollection<ApiKeyItem> _groqKeys = new ObservableCollection<ApiKeyItem>();

        public event EventHandler<bool>? SettingsClosed;

        public SettingsPage(AccountCacheSnapshot? accountSnapshot = null)
        {
            InitializeComponent();

            _settings = SettingsManager.Load();
            _accountSnapshot = accountSnapshot;
            var store = new SqliteRuntimeStore(SettingsManager.GetSettingsPath());
            _contextPackService = new LocalContextPackService(new SqliteContextPackRepository(store));

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
        }

        private void InitializeControls()
        {
            // ✅ USE REGISTRY - AI Providers
            foreach (var provider in AIModelRegistry.GetAllProviders())
            {
                AIProviderComboBox.Items.Add(provider);
            }

            // ✅ USE REGISTRY - ChatGPT Models
            foreach (var model in AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.ChatGPT))
            {
                ChatGPTModelBox.Items.Add(model);
            }

            // ✅ USE REGISTRY - Claude Models
            foreach (var model in AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Claude))
            {
                ClaudeModelBox.Items.Add(model);
            }

            // ✅ USE REGISTRY - Mistral Models
            foreach (var model in AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Mistral))
            {
                MistralModelBox.Items.Add(model);
            }

            // ✅ USE REGISTRY - Gemini Models
            foreach (var model in AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Gemini))
            {
                GeminiModelBox.Items.Add(model);
            }

            // ✅ USE REGISTRY - Groq Models
            foreach (var model in AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Groq))
            {
                GroqModelBox.Items.Add(model);
            }
        }


        private void LoadSettings()
        {
            AIProviderComboBox.SelectedItem = _settings.SelectedAI;
            
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
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("LOADING SETTINGS PAGE");
            Log.WriteLine($"  ChatGPT model from settings: {chatGPTModel}");
            Log.WriteLine($"  Claude model from settings: {claudeModel}");
            Log.WriteLine($"  Mistral model from settings: {mistralModel}");
            Log.WriteLine($"  Gemini model from settings: {geminiModel}");
            Log.WriteLine($"  Groq model from settings: {groqModel}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            // Set selected models in ComboBoxes
            ChatGPTModelBox.SelectedItem = chatGPTModel;
            ClaudeModelBox.SelectedItem = claudeModel;
            MistralModelBox.SelectedItem = mistralModel;
            GeminiModelBox.SelectedItem = geminiModel;
            GroqModelBox.SelectedItem = groqModel;

            VoiceInputCheckBox.IsChecked = _settings.VoiceInputEnabled;
            
            // Rotation settings
            AutoSwitchKeysCheckBox.IsChecked = _settings.AutoSwitchKeysOnError;
            AutoSwitchModelsCheckBox.IsChecked = _settings.AutoSwitchModelsOnError;
            
            UseFakeCursorCheckBox.IsChecked = _settings.UseFakeCursor;
            
            _isUpdatingSlider = true;
            
            double displayValue = _settings.FakeCursorSize * 100;
            if (displayValue < 50) displayValue = 100;
            if (displayValue > 200) displayValue = 100;
            
            FakeCursorSizeSlider.Value = displayValue;
            FakeCursorSizeTextBox.Text = $"{displayValue:F0}";
            
            _isUpdatingSlider = false;
            
            UpdateFakeCursorPanelVisibility();
            SystemPromptBox.Text = _settings.SystemPrompt;

            var selectedPack = _contextPackService.GetSelectedPack();
            ResumeBox.Text = selectedPack.ResumeText;
            UpdateResumeWordCount();
            UpdateResumeSummaryStatus(selectedPack);

            JobDescriptionBox.Text = selectedPack.JobDescriptionText;
            UpdateJobDescriptionWordCount();
            UpdateJobDescriptionSummaryStatus(selectedPack);

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
            UpdatePanelVisibility();
        }

        private void JobDescriptionBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateJobDescriptionWordCount();
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
            if (IsPremiumAccount())
            {
                ChatGPTPanel.Visibility = Visibility.Collapsed;
                ClaudePanel.Visibility = Visibility.Collapsed;
                MistralPanel.Visibility = Visibility.Collapsed;
                GeminiPanel.Visibility = Visibility.Collapsed;
                GroqPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (AIProviderComboBox.SelectedItem == null) return;

            var selected = AIProviderComboBox.SelectedItem as string;

            ChatGPTPanel.Visibility = selected == "ChatGPT" ? Visibility.Visible : Visibility.Collapsed;
            ClaudePanel.Visibility = selected == "Claude" ? Visibility.Visible : Visibility.Collapsed;
            MistralPanel.Visibility = selected == "Mistral" ? Visibility.Visible : Visibility.Collapsed;
            GeminiPanel.Visibility = selected == "Gemini" ? Visibility.Visible : Visibility.Collapsed;
            GroqPanel.Visibility = selected == "Groq" ? Visibility.Visible : Visibility.Collapsed;
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
                _settings.SelectedAI = AIProviderComboBox.SelectedItem as string ?? "ChatGPT";
                
                if (IsPremiumAccount())
                {
                    _settings.ChatGPTApiKeys = new System.Collections.Generic.List<string>();
                    _settings.ClaudeApiKeys = new System.Collections.Generic.List<string>();
                    _settings.MistralApiKeys = new System.Collections.Generic.List<string>();
                    _settings.GeminiApiKeys = new System.Collections.Generic.List<string>();
                    _settings.GroqApiKeys = new System.Collections.Generic.List<string>();
                }
                else
                {
                    _settings.ChatGPTApiKeys = _chatGPTKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.ClaudeApiKeys = _claudeKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.MistralApiKeys = _mistralKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.GeminiApiKeys = _geminiKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                    _settings.GroqApiKeys = _groqKeys.Select(k => k.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
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
                Log.WriteLine("═══════════════════════════════════════════════════════");
                
                // Save legacy single keys (use first key if available)
                _settings.ChatGPTApiKey = _settings.ChatGPTApiKeys.FirstOrDefault() ?? "";
                _settings.ClaudeApiKey = _settings.ClaudeApiKeys.FirstOrDefault() ?? "";
                _settings.MistralApiKey = _settings.MistralApiKeys.FirstOrDefault() ?? "";
                _settings.GeminiApiKey = _settings.GeminiApiKeys.FirstOrDefault() ?? "";
                _settings.GroqApiKey = _settings.GroqApiKeys.FirstOrDefault() ?? "";
                
                // Save models
                _settings.ChatGPTModel = ChatGPTModelBox.SelectedItem as string ?? "gpt-4";
                _settings.ClaudeModel = ClaudeModelBox.SelectedItem as string ?? "claude-3-sonnet-20240229";
                _settings.MistralModel = MistralModelBox.SelectedItem as string ?? "mistral-large-latest";
                _settings.GeminiModel = GeminiModelBox.SelectedItem as string ?? "gemini-2.5-flash";
                _settings.GroqModel = GroqModelBox.SelectedItem as string ?? "llama-3.3-70b-versatile";

                // Save rotation settings
                _settings.AutoSwitchKeysOnError = AutoSwitchKeysCheckBox.IsChecked == true;
                _settings.AutoSwitchModelsOnError = AutoSwitchModelsCheckBox.IsChecked == true;

                _settings.VoiceInputEnabled = VoiceInputCheckBox.IsChecked == true;
                
                _settings.UseFakeCursor = UseFakeCursorCheckBox.IsChecked == true;
                
                if (double.TryParse(FakeCursorSizeTextBox.Text, out double percentage))
                {
                    _settings.FakeCursorSize = Math.Max(0.5, Math.Min(2.0, percentage / 100.0));
                }
                else
                {
                    _settings.FakeCursorSize = 1.0;
                }
                
                _settings.SystemPrompt = SystemPromptBox.Text;

                // debug mode:
                _settings.DebugModeEnabled = DebugModeCheckBox.IsChecked == true;
                _settings.DebugErrorSimulation = (DebugErrorTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "None";

                if (_settings.DebugModeEnabled)
                {
                    Log.WriteLine($"✓ Debug mode enabled: {_settings.DebugErrorSimulation}");
                }
                
                SettingsManager.Save(_settings);

                var selectedPack = _contextPackService.GetSelectedPack();
                var oldResume = selectedPack.ResumeText;
                var oldJobDescription = selectedPack.JobDescriptionText;
                selectedPack.ResumeText = ResumeBox.Text;
                selectedPack.JobDescriptionText = JobDescriptionBox.Text;

                if (oldResume != selectedPack.ResumeText)
                {
                    selectedPack.ResumeSummary = string.Empty;
                    Log.WriteLine("Resume changed - cached summary cleared");
                }

                if (oldJobDescription != selectedPack.JobDescriptionText)
                {
                    selectedPack.JobDescriptionSummary = string.Empty;
                    Log.WriteLine("Job description changed - cached summary cleared");
                }

                _contextPackService.SaveSelectedPack(selectedPack);

                Log.WriteLine($"✓ Settings saved:");
                Log.WriteLine($"  ChatGPT keys: {_settings.ChatGPTApiKeys.Count}");
                Log.WriteLine($"  Claude keys: {_settings.ClaudeApiKeys.Count}");
                Log.WriteLine($"  Mistral keys: {_settings.MistralApiKeys.Count}");
                Log.WriteLine($"  Gemini keys: {_settings.GeminiApiKeys.Count}");
                Log.WriteLine($"  Groq keys: {_settings.GroqApiKeys.Count}");
                Log.WriteLine($"  Auto-switch keys: {_settings.AutoSwitchKeysOnError}");
                Log.WriteLine($"  Auto-switch models: {_settings.AutoSwitchModelsOnError}");

                SettingsClosed?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error saving settings: {ex.Message}");
                InvisibleMessageBox.Show($"Error saving settings:\n\n{ex.Message}", "Error");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsClosed?.Invoke(this, false);
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
            var isPremium = IsPremiumAccount();
            PremiumManagedNotice.Visibility = isPremium ? Visibility.Visible : Visibility.Collapsed;
            ByoConfigurationSection.Visibility = isPremium ? Visibility.Collapsed : Visibility.Visible;
            UpdatePanelVisibility();
        }

        private bool IsPremiumAccount()
        {
            return string.Equals(_accountSnapshot?.AccessTier, "premium", StringComparison.OrdinalIgnoreCase);
        }

        private bool CanAddProviderKey(ObservableCollection<ApiKeyItem> providerKeys, string providerName)
        {
            if (IsPremiumAccount())
            {
                InvisibleMessageBox.Show("Premium accounts do not expose BYO provider configuration.", "Premium Managed AI");
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
            if (providerKeys.Count == 0 && activeProviders > MaxProvidersForByo)
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
            return count;
        }

        private void ValidateByoProviderLimits()
        {
            if (IsPremiumAccount())
            {
                return;
            }

            var providerLists = new[]
            {
                new { Name = "ChatGPT", Keys = _settings.ChatGPTApiKeys },
                new { Name = "Claude", Keys = _settings.ClaudeApiKeys },
                new { Name = "Mistral", Keys = _settings.MistralApiKeys },
                new { Name = "Gemini", Keys = _settings.GeminiApiKeys },
                new { Name = "Groq", Keys = _settings.GroqApiKeys }
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
}
