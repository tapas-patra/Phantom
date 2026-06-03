using System.Windows;

namespace SecureOverlay
{
    public partial class InvisibleMessageBox : Window
    {
        public bool Result { get; private set; } = false;

        public InvisibleMessageBox(string message, string title = "Message", bool showCancel = false)
        {
            InitializeComponent();
            WindowProtection.MakeInvisibleToScreenCapture(this);

            TitleText.Text = title;
            MessageText.Text = message;

            if (showCancel)
            {
                CancelButton.Visibility = Visibility.Visible;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        public static bool Show(string message, string title = "Message")
        {
            var box = new InvisibleMessageBox(message, title, false);
            box.ShowDialog();
            return box.Result;
        }

        public static bool ShowYesNo(string message, string title = "Confirm")
        {
            var box = new InvisibleMessageBox(message, title, true);
            box.OkButton.Content = "Yes";
            box.ShowDialog();
            return box.Result;
        }
    }
}
