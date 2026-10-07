using System.Windows;
using AcerCareLite.Core.Utilities;

namespace AcerCareLite.App;

public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
