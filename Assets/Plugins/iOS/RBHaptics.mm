// Native haptics for HapticManager.cs (iOS only).
//
// The generators are kept as file-static singletons so they are created once
// and never leak, and so `prepare` can warm the Taptic Engine up before the
// pulse (which is what makes iOS haptics feel instant instead of laggy).
//
// Type ids must stay in sync with the HapticType enum in HapticManager.cs:
//   0 Selection, 1 Light, 2 Medium, 3 Heavy,
//   4 Success,   5 Warning, 6 Failure, 7 Win, 8 Lose,
//   9 Strike,   10 DoubleTap, 11 LongHeavy, 12 CountdownPulse
//
// iOS has no multi-pulse or sustained-vibration API, so DoubleTap (10),
// CountdownPulse (12) and Rumble are composed on the C# side from repeated
// single impacts; this file only ever fires one impact per call.

#import <UIKit/UIKit.h>
#import <AudioToolbox/AudioToolbox.h>

static UISelectionFeedbackGenerator*    gSelection    = nil;
static UIImpactFeedbackGenerator*       gImpactLight  = nil;
static UIImpactFeedbackGenerator*       gImpactMedium = nil;
static UIImpactFeedbackGenerator*       gImpactHeavy  = nil;
static UINotificationFeedbackGenerator* gNotification = nil;

extern "C" {

bool RBHapticsSupported()
{
    if (@available(iOS 10.0, *)) { return true; }
    return true; // older devices still get the classic system vibration
}

void RBHapticsPlay(int type)
{
    if (@available(iOS 10.0, *))
    {
        switch (type)
        {
            case 0: // Selection
                if (gSelection == nil) gSelection = [[UISelectionFeedbackGenerator alloc] init];
                [gSelection prepare];
                [gSelection selectionChanged];
                return;

            case 1: // Light
                if (gImpactLight == nil)
                    gImpactLight = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
                [gImpactLight prepare];
                [gImpactLight impactOccurred];
                return;

            case 2: // Medium
                if (gImpactMedium == nil)
                    gImpactMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                [gImpactMedium prepare];
                [gImpactMedium impactOccurred];
                return;

            case 12: // CountdownPulse -> first beat of the urgency pattern
                if (gImpactMedium == nil)
                    gImpactMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                [gImpactMedium prepare];
                [gImpactMedium impactOccurred];
                return;

            case 3:  // Heavy
            case 9:  // Strike     -> hardest single impact iOS offers
            case 10: // DoubleTap  -> first of two, C# schedules the second
            case 11: // LongHeavy  -> no duration control on iOS
                if (gImpactHeavy == nil)
                    gImpactHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
                [gImpactHeavy prepare];
                [gImpactHeavy impactOccurred];
                return;

            case 4: // Success
            case 7: // Win  -> success notification, the closest system pattern
                if (gNotification == nil) gNotification = [[UINotificationFeedbackGenerator alloc] init];
                [gNotification prepare];
                [gNotification notificationOccurred:UINotificationFeedbackTypeSuccess];
                return;

            case 5: // Warning
            case 8: // Lose -> warning notification (soft double, not alarming)
                if (gNotification == nil) gNotification = [[UINotificationFeedbackGenerator alloc] init];
                [gNotification prepare];
                [gNotification notificationOccurred:UINotificationFeedbackTypeWarning];
                return;

            case 6: // Failure
                if (gNotification == nil) gNotification = [[UINotificationFeedbackGenerator alloc] init];
                [gNotification prepare];
                [gNotification notificationOccurred:UINotificationFeedbackTypeError];
                return;

            default:
                return;
        }
    }

    AudioServicesPlaySystemSound(kSystemSoundID_Vibrate);
}

} // extern "C"
