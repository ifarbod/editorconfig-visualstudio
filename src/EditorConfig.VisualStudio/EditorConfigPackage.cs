﻿using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace EditorConfig.VisualStudio
{
    using Helpers;
    using Integration;
    using Integration.Commands;
    using Integration.Events;

    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400, LanguageIndependentName = "EditorConfig")]
    [ProvideAutoLoad(UIContextGuid, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(Guids.GuidEditorConfigPkgString)]
    public sealed class EditorConfigPackage : AsyncPackage
    {
        #region Fields

        /// <summary>
        /// An internal collection of the commands registered by this package.
        /// </summary>
        private readonly ICollection<BaseCommand> _commands = new List<BaseCommand>();

        /// <summary>
        /// The top level application instance of the VS IDE that is executing this package.
        /// </summary>
        private DTE2 _ide;

        #endregion

        #region Constructors

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require
        /// any Visual Studio service because at this point the package object is created but
        /// not sited yet inside Visual Studio environment. The place to do all the other
        /// initialization is the Initialize method.
        /// </summary>
        public EditorConfigPackage()
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this));

            Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        #endregion Constructors

        #region Public Integration Properties

        /// <summary>
        /// Gets the top level application instance of the VS IDE that is executing this package.
        /// </summary>
        public DTE2 IDE
        {
            get { return _ide ?? (_ide = (DTE2)GetService(typeof(DTE))); }
        }

        /// <summary>
        /// Gets the version of the running IDE instance.
        /// </summary>
        public Version IDEVersion { get { return new Version(IDE.Version); } }

        /// <summary>
        /// Gets the menu command service.
        /// </summary>
        public OleMenuCommandService MenuCommandService
        {
            get { return GetService(typeof(IMenuCommandService)) as OleMenuCommandService; }
        }

        /// <summary>
        /// Gets a flag indicating if POSIX regular expressions should be used for TextDocument Find/Replace actions.
        /// Applies to pre-Visual Studio 11 versions.
        /// </summary>
        public bool UsePOSIXRegEx
        {
            get { return IDEVersion.Major < 11; }
        }

        /// <summary>
        /// Gets the currently active document, otherwise null.
        /// </summary>
        public Document ActiveDocument
        {
            get
            {
                try
                {
                    return IDE.ActiveDocument;
                }
                catch (Exception)
                {
                    // If a project property page is active, accessing the ActiveDocument causes an exception.
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets the component model for the session.
        /// </summary>
        public IComponentModel ComponentModel
        {
            get { return GetService(typeof(SComponentModel)) as IComponentModel; }
        }

        /// <summary>
        /// Gets or sets a flag indicating if EditorConfig is running inside an AutoSave context.
        /// </summary>
        public static bool IsAutoSaveContext { get; set; }

        #endregion Public Integration Properties

        #region Private Event Listener Properties

        /// <summary>
        /// Gets or sets the running document table event listener.
        /// </summary>
        private RunningDocumentTableEventListener RunningDocumentTableEventListener { get; set; }

        /// <summary>
        /// Gets or sets the shell event listener.
        /// </summary>
        private ShellEventListener ShellEventListener { get; set; }

        #endregion Private Event Listener Properties

        #region Private Service Properties

        /// <summary>
        /// Gets the shell service.
        /// </summary>
        private IVsShell ShellService
        {
            get { return GetService(typeof(SVsShell)) as IVsShell; }
        }

        #endregion Private Service Properties

        #region Package Members

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this));

            // Switches to the UI thread in order to consume some services used in command initialization
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Query service asynchronously from the UI thread
            var dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;

            RegisterCommands();
            RegisterShellEventListener();
        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        public void Initialize_old()
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this));
            base.Initialize();


        }

        #endregion Package Members

        #region IVsInstalledProduct Members

        public int IdBmpSplash(out uint pIdBmp)
        {
            pIdBmp = 400;
            return VSConstants.S_OK;
        }

        public int IdIcoLogoForAboutbox(out uint pIdIco)
        {
            pIdIco = 400;
            return VSConstants.S_OK;
        }

        public int OfficialName(out string pbstrName)
        {
            pbstrName = GetResourceString("@110");
            return VSConstants.S_OK;
        }

        public int ProductDetails(out string pbstrProductDetails)
        {
            pbstrProductDetails = GetResourceString("@112");
            return VSConstants.S_OK;
        }

        public int ProductID(out string pbstrPID)
        {
            pbstrPID = GetResourceString("@114");
            return VSConstants.S_OK;
        }

        public string GetResourceString(string resourceName)
        {
            string resourceValue;
            var resourceManager = (IVsResourceManager)GetService(typeof(SVsResourceManager));
            if (resourceManager == null)
            {
                throw new InvalidOperationException(
                    "Could not get SVsResourceManager service. Make sure that the package is sited before calling this method");
            }

            var packageGuid = GetType().GUID;
            var hr = resourceManager.LoadResourceString(
                ref packageGuid, -1, resourceName, out resourceValue);
            ErrorHandler.ThrowOnFailure(hr);

            return resourceValue;
        }

        #endregion IVsInstalledProduct Members

        #region Private Methods

        /// <summary>
        /// Called when a DispatcherUnhandledException is raised by Visual Studio.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="DispatcherUnhandledExceptionEventArgs" /> instance containing the event data.</param>
        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            OutputWindowHelper.WriteLine("EditorConfig's diagnostics mode caught the following unhandled exception in Visual Studio--" + Environment.NewLine + e.Exception);
            e.Handled = true;
        }

        /// <summary>
        /// Register the package commands (which must exist in the .vsct file).
        /// </summary>
        private void RegisterCommands()
        {
            var menuCommandService = MenuCommandService;
            if (menuCommandService == null) return;

            // Create the individual commands, which internally register for command events.
            _commands.Add(new CleanupActiveCodeCommand(this));

            // Add all commands to the menu command service.
            foreach (var command in _commands)
            {
                menuCommandService.AddCommand(command);
            }
        }

        /// <summary>
        /// Registers the shell event listener.
        /// </summary>
        /// <remarks>
        /// This event listener is registered by itself and first to wait for the shell to be ready
        /// for other event listeners to be registered.
        /// </remarks>
        private void RegisterShellEventListener()
        {
            ShellEventListener = new ShellEventListener(this, ShellService);
            ShellEventListener.ShellAvailable += RegisterNonShellEventListeners;
        }

        /// <summary>
        /// Register the package event listeners.
        /// </summary>
        /// <remarks>
        /// This must occur after the DTE service is available since many of the events
        /// are based off of the DTE object.
        /// </remarks>
        private void RegisterNonShellEventListeners()
        {
            // Create event listeners and register for events.
            var cleanupActiveCodeCommand = new CleanupActiveCodeCommand(this);

            RunningDocumentTableEventListener = new RunningDocumentTableEventListener(this);
            RunningDocumentTableEventListener.BeforeSave += cleanupActiveCodeCommand.OnBeforeDocumentSave;
        }

        #endregion Private Methods

        #region IDisposable Members

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Dispose of any event listeners.
            if (RunningDocumentTableEventListener != null)
            {
                RunningDocumentTableEventListener.Dispose();
            }

            if (ShellEventListener != null)
            {
                ShellEventListener.Dispose();
            }
        }

        #endregion IDisposable Members

    }
}
