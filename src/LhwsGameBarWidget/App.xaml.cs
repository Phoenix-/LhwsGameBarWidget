using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LhwsGameBarWidget;

sealed partial class App : Application
{
    private XboxGameBarWidget? sensorsWidget;

    public App()
    {
        InitializeComponent();
        Suspending += OnSuspending;
    }

    protected override void OnActivated(IActivatedEventArgs args)
    {
        XboxGameBarWidgetActivatedEventArgs? widgetArgs = null;
        if (args.Kind == ActivationKind.Protocol)
        {
            var protocolArgs = args as IProtocolActivatedEventArgs;
            if (protocolArgs?.Uri.Scheme == "ms-gamebarwidget")
            {
                widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
            }
        }
        if (widgetArgs == null)
        {
            return;
        }

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
