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

            Log($"widget activation, isLaunch={widgetArgs.IsLaunchActivation} uri={widgetArgs.Uri}");
            if (widgetArgs.IsLaunchActivation)
            {
                var rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;

                // Bootstraps the connection with Game Bar; must be kept alive for the widget's lifetime
                sensorsWidget = new XboxGameBarWidget(
                    widgetArgs,
                    Window.Current.CoreWindow,
                    rootFrame);
                rootFrame.Navigate(typeof(WidgetPage), sensorsWidget);

                Window.Current.Closed += WidgetWindow_Closed;
                Window.Current.Activate();
                Log("widget bootstrapped");
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

    private void WidgetWindow_Closed(object sender, Windows.UI.Core.CoreWindowEventArgs e)
    {
        sensorsWidget = null;
        Window.Current.Closed -= WidgetWindow_Closed;
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }

    private void OnSuspending(object sender, SuspendingEventArgs e)
    {
        var deferral = e.SuspendingOperation.GetDeferral();
        sensorsWidget = null;
        deferral.Complete();
    }
}
