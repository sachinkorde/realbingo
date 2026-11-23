using UnityEngine;

/// <summary>
/// Opens one contact link - a WhatsApp chat, a mailto: address, a phone dialler,
/// a web page. Wire a Button's OnClick to <see cref="Open"/> and set the url.
///
/// Android opens whichever app claims the scheme, so the link does nothing
/// visible on a device with no WhatsApp, no mail client or no SIM. That is the
/// platform's behaviour, not a failure here - which is why the number and the
/// address are also shown as plain text beside each button, so they can always
/// be read and copied by hand.
///
/// Typical urls:
///   https://wa.me/919000000000   WhatsApp chat (country code, no + and no spaces)
///   mailto:support@example.com   email
///   tel:+919000000000            dialler
/// </summary>
public class ContactLink : MonoBehaviour
{
    [Tooltip("https://wa.me/<number>, mailto:<address>, tel:<number>, or a web address.")]
    [SerializeField] private string url;

    /// <summary>What the url is set to, for a label that has to match it.</summary>
    public string Url => url;

    public void Open()
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogWarning($"[ContactLink] '{name}' has no url set.", this);
            return;
        }

        Application.OpenURL(url);
    }
}
