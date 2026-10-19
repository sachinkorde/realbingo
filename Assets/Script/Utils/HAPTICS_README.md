# Vibration & Button Press Feedback

Everything here is additive — no existing behaviour was changed, no scene or
prefab was modified. If you delete the four new scripts the project goes back
to exactly how it was.

## What was added

| File | What it does |
|---|---|
| `Assets/Script/Utils/HapticManager.cs` | Cross-platform vibration. Static — call it from anywhere, no scene object. |
| `Assets/Script/UI/ButtonFeedback.cs` | Press animation (squash on down, spring back on up) + vibration for one control. |
| `Assets/Script/UI/UIFeedbackInstaller.cs` | Boots itself at start-up and puts `ButtonFeedback` on **every** Button and Toggle in the game, including ones spawned at runtime. |
| `Assets/Plugins/iOS/RBHaptics.mm` | Native iOS haptics (Taptic Engine). |
| `Assets/Editor/RBAndroidVibratePermission.cs` | Adds `android.permission.VIBRATE` to the Android build automatically. |
| `Assets/Editor/RBButtonFeedbackBaker.cs` | Optional menu item to bake `ButtonFeedback` into scenes instead of adding it at runtime. |

## Nothing to wire up

Press Play and it works. `UIFeedbackInstaller` finds every Button/Toggle,
including inactive ones and ones instantiated later (notification rows,
referral rows, popups), and re-scans on a backing-off timer (2s → 8s) so it
costs nothing while the UI is idle.

## Where vibration fires

**Buttons (automatic, every control in the game)**
- Any Button / Toggle press → `Light` tap + press animation
- A control that is *not* interactable gives no tap and no animation

**Betting**
- Chip selected → `Selection`
- Bet placed on a number → `Medium`
- Bet refused / not enough balance → `Failure`
- Repeat last bets → `Heavy`
- Clear table → `Medium`
- Undo last bet → `Light`

**Round**
- Last 3 seconds of the countdown → `Light` on each second
- Betting closed → `Warning`
- Wheel lands → `Heavy`
- **Round won → `Win`** (rising triple-tap)
- **Round lost → `Lose`** (single soft fading buzz)

**Money & messages**
- Promo code accepted / rejected → `Success` / `Failure`
- Payment succeeded / failed → `Success` / `Failure`
- Every Toast: Success → `Success`, Error → `Failure`, Warning → `Warning`
  (this covers login, OTP, withdrawals, transfers, referrals, version checks —
  they all route through `Toast`)

**Panels**
- Side panel opened → `Light`, closed → `Selection`

## Using it in new code

```csharp
HapticManager.Light();                  // button tap
HapticManager.Selection();              // chip / toggle / tab
HapticManager.Medium();                 // confirm
HapticManager.Heavy();                  // big commit
HapticManager.Success();                // deposit ok
HapticManager.Failure();                // rejected
HapticManager.Win();                    // round won
HapticManager.Lose();                   // round lost
HapticManager.Custom(40, 160);          // 40 ms at amplitude 160/255 (Android)
HapticManager.Cancel();                 // stop whatever is buzzing
```

Safe to call on any platform — it is a no-op in the Editor and on devices with
no vibrator.

## Player on/off switch

```csharp
HapticManager.Enabled = false;   // saved to PlayerPrefs "HapticsEnabled"
HapticManager.Toggle();          // pulses once when switched ON, as confirmation
```

`SettingsPanel` now has four **optional** fields — `Vibration Button`,
`Vibration Icon Image`, `Vibration On Sprite`, `Vibration Off Sprite`. Assign
them and the toggle wires itself; leave them empty and the panel behaves
exactly as before. You can also hook `SettingsPanel.OnVibrationToggleClicked`
straight to a button's OnClick in the Inspector.

Vibration is deliberately **separate** from the sound toggle: players mute the
game in public but still want to feel the result.

## Tuning

Per control, in the Inspector on `ButtonFeedback`:
`pressedScale` (0.93 default), `pressDuration`, `releaseDuration`,
`animateOnPress`, `hapticOnPress`, `hapticType`, and `target` (scale a child
instead of the button root).

Adding `ButtonFeedback` to a control by hand takes priority — the installer
never overwrites a component that is already there.

Globally, before the first scene loads:
```csharp
UIFeedbackInstaller.AutoInstall = false;      // turn the whole thing off
UIFeedbackInstaller.ButtonHaptic = HapticType.Selection;   // softer taps everywhere
```

On `SpinManager` there is a `playPegTickHaptic` toggle (off by default) if you
want the wheel to buzz on every peg. It is off because a continuous 6–8 second
rumble drains the battery and gets annoying fast.

On `TimerController`: `vibrateOnFinalSeconds` and `vibrateFromSeconds`.

## Android notes

- The `VIBRATE` permission is injected into the generated manifest at build
  time, so no manifest of your own is needed and nothing existing is touched.
- Amplitude control needs Android 8 (API 26). Older devices still vibrate, just
  at a fixed strength. Android 12+ goes through `VibratorManager`.
- If anything fails on an exotic ROM, haptics silently switch themselves off —
  the game never crashes over a vibration.

## iOS notes

`RBHaptics.mm` uses the standard `UIImpact` / `UISelection` /
`UINotificationFeedbackGenerator`. Devices older than iPhone 7 fall back to the
classic system vibration. The generators are cached and `prepare`d, which is
what makes the haptic feel instant rather than lagging behind the tap.
