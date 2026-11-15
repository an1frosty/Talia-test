using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinSetup.UI;

namespace WinSetup;

/// <summary>Themed replacement for MessageBox / InputBox. Same call shape as MessageBox.Show.</summary>
public partial class TaliaDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;
    private MessageBoxResult _escapeResult = MessageBoxResult.Cancel;
    private bool _isPrompt;

    private TaliaDialog()
    {
        InitializeComponent();
        TitleBar.Apply(this);
        TitleBar.FitToWorkArea(this);
    }

    public static MessageBoxResult Show(string text, string caption = "Talia", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new TaliaDialog();
        dialog.Configure(text, caption, icon);
        switch (buttons)
        {
            case MessageBoxButton.OK:
                dialog._escapeResult = MessageBoxResult.OK;
                dialog.AddButton("OK", MessageBoxResult.OK, primary: true);
                break;
            case MessageBoxButton.OKCancel:
                dialog.AddButton("Отмена", MessageBoxResult.Cancel, primary: false);
                dialog.AddButton("OK", MessageBoxResult.OK, primary: true);
                break;
            case MessageBoxButton.YesNo:
                dialog._escapeResult = MessageBoxResult.No;
                dialog.AddButton("Нет", MessageBoxResult.No, primary: false);
                dialog.AddButton("Да", MessageBoxResult.Yes, primary: true);
                break;
            case MessageBoxButton.YesNoCancel:
                dialog.AddButton("Отмена", MessageBoxResult.Cancel, primary: false);
                dialog.AddButton("Нет", MessageBoxResult.No, primary: false);
                dialog.AddButton("Да", MessageBoxResult.Yes, primary: true);
                break;
        }
        dialog.Place();
        dialog.ShowDialog();
        return dialog._result == MessageBoxResult.None ? dialog._escapeResult : dialog._result;
    }

    /// <summary>Returns the entered text, or null if the user cancelled.</summary>
    public static string? Prompt(string message, string caption, string defaultValue = "")
    {
        var dialog = new TaliaDialog { _isPrompt = true };
        dialog.Configure(message, caption, MessageBoxImage.Question);
        dialog.InputBox.Visibility = Visibility.Visible;
        dialog.InputBox.Text = defaultValue;
        dialog.AddButton("Отмена", MessageBoxResult.Cancel, primary: false);
        dialog.AddButton("OK", MessageBoxResult.OK, primary: true);
        dialog.Loaded += (_, _) => { dialog.InputBox.Focus(); dialog.InputBox.SelectAll(); };
        dialog.Place();
        dialog.ShowDialog();
        return dialog._result == MessageBoxResult.OK ? dialog.InputBox.Text : null;
    }

    private void Configure(string text, string caption, MessageBoxImage icon)
    {
        Title = string.IsNullOrWhiteSpace(caption) ? "Talia" : caption;
        TitleText.Text = Title;
        MessageText.Text = text;
        AccentBar.Fill = icon switch
        {
            MessageBoxImage.Error => (System.Windows.Media.Brush)FindResource("BrushRed"),
            MessageBoxImage.Warning => (System.Windows.Media.Brush)FindResource("BrushSkin"),
            MessageBoxImage.Information => (System.Windows.Media.Brush)FindResource("BrushIcy"),
            _ => (System.Windows.Media.Brush)FindResource("BrushPink")
        };
    }

    private void AddButton(string label, MessageBoxResult result, bool primary)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 88,
            Margin = new Thickness(ButtonRow.Children.Count == 0 ? 0 : 8, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            IsDefault = primary,
            IsCancel = result is MessageBoxResult.Cancel or MessageBoxResult.No
        };
        button.Click += (_, _) => { _result = result; Close(); };
        ButtonRow.Children.Add(button);
    }

    private void Place()
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && !ReferenceEquals(w, this))
                    ?? Application.Current?.MainWindow;
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _result = _escapeResult; Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && _isPrompt && InputBox.IsKeyboardFocused) { _result = MessageBoxResult.OK; Close(); e.Handled = true; }
    }
}
