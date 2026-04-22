using System.Windows;

namespace VideoScreensaver {
    public partial class SettingsWindow : Window {
        public SettingsWindow() {
            InitializeComponent();
            Loaded += (s, e) => {
                var vm = DataContext as SettingsViewModel;
                if (vm != null) NasPasswordBox.Password = vm.NasPassword;
            };
            NasPasswordBox.PasswordChanged += (s, e) => {
                var vm = DataContext as SettingsViewModel;
                if (vm != null) vm.NasPassword = NasPasswordBox.Password;
            };
        }
    }
}