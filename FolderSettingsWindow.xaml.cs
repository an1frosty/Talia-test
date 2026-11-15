using System.Windows;
using Microsoft.Win32;
using WinSetup.UI;
namespace WinSetup;
public partial class FolderSettingsWindow : Window
{
    private readonly List<string> _target;
    private readonly List<string> _working;
    public FolderSettingsWindow(List<string> folders)
    {
        InitializeComponent(); TitleBar.Apply(this); TitleBar.FitToWorkArea(this); _target=folders; _working=[..folders]; RefreshList();
    }
    private void RefreshList() { FolderList.ItemsSource=null; FolderList.ItemsSource=_working; }
    private void Preset_Click(object sender,RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.Tag is string p && !_working.Contains(p,StringComparer.OrdinalIgnoreCase)) { _working.Add(p); RefreshList(); } }
    private void AddCustom_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog { Title="Выбери родительскую папку", Multiselect=false };
        if(dialog.ShowDialog(this)==true)
        {
            var name=TaliaDialog.Prompt("Название новой папки:","Новая папка","MyFolder");
            if(!string.IsNullOrWhiteSpace(name)) { var path=System.IO.Path.Combine(dialog.FolderName,name.Trim()); if(!_working.Contains(path,StringComparer.OrdinalIgnoreCase)) _working.Add(path); RefreshList(); }
        }
    }
    private void Remove_Click(object sender,RoutedEventArgs e) { if(FolderList.SelectedItem is string s) { _working.Remove(s); RefreshList(); } }
    private void Close_Click(object sender,RoutedEventArgs e) { _target.Clear(); _target.AddRange(_working); DialogResult=true; Close(); }
}
