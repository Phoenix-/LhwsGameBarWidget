using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;

namespace LhwsGameBarWidget;

sealed partial class App : Application
{
    private XboxGameBarWidget? sensorsWidget;
    private XboxGameBarWidget? settingsWidget;

    public App()
    {
        // The projection assembly is not named after its root namespace, so CsWinRT
        // cannot find it when it materializes Game Bar objects (e.g. activation args);
        // registering it makes typed RCWs resolve properly.
        ComWrappersSupport.RegisterProjectionAssembly(typeof(XboxGameBarWidget).Assembly);
        InitializeComponent();
        Suspending += OnSuspending;
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(ApplicationData.Current.LocalFolder.Path, "widget.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
        }
        catch
        {
            // logging must never take the widget down
        }
    }

    protected override void OnActivated(IActivatedEventArgs args)
    {
        try
        {
            Log($"OnActivated kind={args.Kind}");
            XboxGameBarWidgetActivatedEventArgs? widgetArgs = null;
            if (args.Kind == ActivationKind.Protocol &&
                args is IProtocolActivatedEventArgs protocolArgs &&
                protocolArgs.Uri.Scheme == "ms-gamebarwidget")
            {
                widgetArgs = args as XboxGameBarWidgetActivatedEventArgs
                    ?? XboxGameBarWidgetActivatedEventArgs.FromAbi(((IWinRTObject)args).NativeObject.ThisPtr);
            }
            if (widgetArgs == null)
            {
                Log("not a widget activation, ignoring");
                return;
            }

            Log($"widget activation, isLaunch={widgetArgs.IsLaunchActivation} extId={widgetArgs.AppExtensionId} uri={widgetArgs.Uri}");
            if (widgetArgs.IsLaunchActivation)
            {
                var rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;

                // Bootstraps the connection with Game Bar; must be kept alive for the widget's lifetime.
                // Each widget (main and settings) gets its own view thread and Window.Current.
                var widget = new XboxGameBarWidget(
                    widgetArgs,
                    Window.Current.CoreWindow,
                    rootFrame);
                bool isSettings = widgetArgs.AppExtensionId == "LhwsSensorsSettings";
                if (isSettings)
                {
                    settingsWidget = widget;
                }
                else
                {
                    sensorsWidget = widget;
                }
                rootFrame.Navigate(isSettings ? typeof(SettingsPage) : typeof(WidgetPage), widget);

                Window.Current.Closed += (s, e2) =>
                {
                    if (ReferenceEquals(settingsWidget, widget))
                    {
                        settingsWidget = null;
                    }
                    if (ReferenceEquals(sensorsWidget, widget))
                    {
                        sensorsWidget = null;
                    }
                };
                Window.Current.Activate();
                Log($"widget bootstrapped ({(isSettings ? "settings" : "main")})");
            }
        }
        catch (Exception ex)
        {
            Log($"OnActivated FAILED: {ex}");
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        // Normal launch (outside Game Bar): show a hint page
        if (Window.Current.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            Window.Current.Content = rootFrame;
        }

        if (!e.PrelaunchActivated)
        {
            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(MainPage), e.Arguments);
            }
            Window.Current.Activate();
        }
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }

    private void OnSuspending(object sender, SuspendingEventArgs e)
    {
        var deferral = e.SuspendingOperation.GetDeferral();
        sensorsWidget = null;
        settingsWidget = null;
        deferral.Complete();
    }
}
