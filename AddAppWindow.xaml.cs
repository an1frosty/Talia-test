using System.Windows;
using WinSetup.Models;
using WinSetup.UI;

namespace WinSetup;

public partial class AddAppWindow : Window
{
    public AppEntry? Result { get; private set; }

    private readonly AppEntry? _original;

    public AddAppWindow(AppEntry? existing = null)
    {
        InitializeComponent();
        TitleBar.Apply(this);
        TitleBar.FitToWorkArea(this);
        _original = existing;
        if (existing is not null)
        {
            Title = "Talia — Изменить программу";
            HeaderText.Text = "Изменить программу";
            NameBox.Text = existing.Name;
            CategoryBox.Text = existing.Category;
            DescriptionBox.Text = existing.Description;
            IdBox.Text = existing.WingetId;
            ((System.Windows.Controls.Button)FindName("SaveButton")!).Content = "Сохранить";
        }
        NameBox.Focus();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var category = CategoryBox.Text.Trim();
        var description = DescriptionBox.Text.Trim();
        var id = IdBox.Text.Trim();

        if (name.Length is < 2 or > 80) { ErrorText.Text = "Название должно содержать от 2 до 80 символов."; return; }
        if (category.Length is < 2 or > 40) { ErrorText.Text = "Укажи категорию длиной от 2 до 40 символов."; return; }
        if (id.Length is < 2 or > 120 || !char.IsAsciiLetterOrDigit(id[0]) || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
        { ErrorText.Text = "Укажи точный Package ID WinGet: латинские буквы, цифры, точка, дефис или подчёркивание."; return; }

        Result = new AppEntry
        {
            Name = name,
            Category = category,
            Description = string.IsNullOrWhiteSpace(description) ? "Пользовательская программа." : description,
            WingetId = id,
            IsCustom = true,
            IsSelected = _original?.IsSelected ?? false,
            Status = _original?.Status ?? "Не проверено"
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
