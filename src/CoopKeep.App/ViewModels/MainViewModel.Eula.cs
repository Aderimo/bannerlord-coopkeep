using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Sunucu lisans sözleşmesinin (EULA) onayı.
/// </summary>
/// <remarks>
/// <para>
/// Coop v0.1.6 sunucusu ilk açılışta EULA onayı istiyor; onay olmadan barındırma
/// başlamıyor. Onayı CoopKeep'in kullanıcı adına sessizce vermesi doğru olmaz: sözleşme
/// kullanıcı ile mod geliştiricisi arasında. Bu yüzden metin bir kez gösterilir,
/// kullanıcı "Kabul ediyorum" derse sunucu <c>--accept-eula</c> ile başlatılır.
/// </para>
/// <para>
/// Sözleşme metni değişirse (mod güncellemesi) parmak izi tutmaz ve onay yeniden istenir.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    [ObservableProperty] private bool _eulaOpen;
    [ObservableProperty] private string _eulaText = "";

    /// <summary>Onaydan sonra yapılacak iş (başlatma ya da dünya kurma).</summary>
    private Func<Task>? _afterEula;

    /// <summary>
    /// CoopKeep modunun sunucusunda ayrı bir lisans sözleşmesi yok (mod MIT lisanslı); eski
    /// sunucunun sözleşmesi yalnızca o kuruluysa sorulurdu.
    /// </summary>
    private bool EulaAccepted =>
        _coop is null || (_coop.EulaFingerprint() is { } fp && string.Equals(_ui.AcceptedEulaFingerprint, fp, StringComparison.Ordinal));

    /// <summary>
    /// EULA onaylıysa işi hemen yapar; değilse sözleşmeyi gösterir ve onaydan sonra yapar.
    /// </summary>
    /// <returns>İş hemen yapıldıysa <see langword="true"/>.</returns>
    private async Task<bool> RequireEulaAsync(Func<Task> action)
    {
        if (EulaAccepted || _coop is null)
        {
            await action();
            return true;
        }

        try
        {
            EulaText = File.ReadAllText(_coop.EulaPath);
        }
        catch (IOException)
        {
            EulaText = L["eula.missing"];
        }

        _afterEula = action;
        EulaOpen = true;
        return false;
    }

    [RelayCommand]
    private async Task AcceptEulaAsync()
    {
        _ui.AcceptedEulaFingerprint = _coop?.EulaFingerprint();
        _ui.Save();
        EulaOpen = false;

        var next = _afterEula;
        _afterEula = null;
        if (next is not null) await next();
    }

    [RelayCommand]
    private void DeclineEula()
    {
        EulaOpen = false;
        _afterEula = null;
        WizardCreating = false;
        ShowToast(L["eula.declined"], ToastKind.Warning);
    }

    [RelayCommand]
    private void OpenLicense()
    {
        if (_coop is not null) OpenPath(_coop.LicensePath);
    }
}
