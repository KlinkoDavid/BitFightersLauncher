using System.Windows;

namespace BitFightersLauncher
{
    public partial class SimpleLoginWindow : Window
    {
        public SimpleLoginWindow()
        {
            InitializeComponent();
        }

        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Login gomb mûködik!");
            this.Close();
        }
    }
}