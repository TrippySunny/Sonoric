using CommunityToolkit.Mvvm.ComponentModel;

namespace Sonoric.ViewModels;

public partial class NamePromptViewModel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? validationMessage;

    public bool TryAccept()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = "Укажите имя";
            return false;
        }

        Name = Name.Trim();
        ValidationMessage = null;
        return true;
    }
}
