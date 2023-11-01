// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Connect;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

using AsyncOperation = UnityEngine.AsyncOperation;
using Button = UnityEngine.UIElements.Button;

namespace UnityEditor.Connect
{
    /// <summary>
    /// The In App Purchasing section of the CloudServices project settings (actually simply called Services in the ProjectSettings)
    /// Uses a simple state machine to keep track of current state.
    /// </summary>
    class PurchasingProjectSettings : ServicesProjectSettings
    {
        //Resources
        const string k_ServiceName = "Purchasing";
        const string k_ProjectSettingsPath = "Project/Services/In-App Purchasing";

        const string k_PurchasingServicesTemplatePath = "UXML/ServicesWindow/PurchasingProjectSettings.uxml";

        //State Names
        const string k_StateNameDisabled = "DisabledState";
        const string k_StateNameEnabled = "EnabledState";

        //Keywords
        const string k_KeywordPurchasing = "purchasing";
        const string k_KeywordInApp = "in-app";
        const string k_KeywordPurchase = "purchase";
        const string k_KeywordRevenue = "revenue";
        const string k_KeywordPlatforms = "platforms";
        const string k_KeywordGooglePlay = "Google Play"; //So devs can find where to place their Google Play Public Key
        const string k_KeywordPublicKey = "public key"; //So devs can find where to place their Google Play Public Key

        const string k_PurchasingPermissionMessage = "You do not have sufficient permissions to enable / disable Purchasing service.";
        const string k_PurchasingPackageName = "In-App Purchasing Package";
        const string k_GoToDashboardLink = "GoToDashboard";
        const string k_ServiceToggleClassName = "service-toggle";
        const string k_ServiceNameProperty = "serviceName";
        bool m_CallbacksInitialized;

        const string k_WelcomeToIapBlock = "WelcomeToIapBlock";
        const int k_UnityAnalyticsSupportMajorVersion = 4;
        const int k_UnityAnalyticsSupportMinorVersion = 2;

        Toggle m_MainServiceToggle;
        VisualElement m_GoToDashboard;


        [SettingsProvider]
        public static SettingsProvider CreateServicesProvider()
        {
            return new PurchasingProjectSettings(k_ProjectSettingsPath, SettingsScope.Project, new List<string>()
            {
                L10n.Tr(k_KeywordPurchasing),
                L10n.Tr(k_KeywordInApp),
                L10n.Tr(k_KeywordPurchase),
                L10n.Tr(k_KeywordRevenue),
                L10n.Tr(k_KeywordPlatforms),
                L10n.Tr(k_KeywordGooglePlay),
                L10n.Tr(k_KeywordPublicKey)
            });
        }

        SimpleStateMachine<ServiceEvent> m_StateMachine;

        DisabledState m_DisabledState;
        EnabledState m_EnabledState;

        public PurchasingProjectSettings(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, k_ServiceName, keywords)
        {
            m_StateMachine = new SimpleStateMachine<ServiceEvent>();

            m_StateMachine.AddEvent(ServiceEvent.Disabled);
            m_StateMachine.AddEvent(ServiceEvent.Enabled);

            m_DisabledState = new DisabledState(m_StateMachine, this);
            m_EnabledState = new EnabledState(m_StateMachine, this);

            m_StateMachine.AddState(m_DisabledState);
            m_StateMachine.AddState(m_EnabledState);
        }

        void OnDestroy()
        {
            FinalizeServiceCallbacks();
        }

        protected override Notification.Topic[] notificationTopicsToSubscribe => new[]
        {
            Notification.Topic.PurchasingService,
            Notification.Topic.ProjectBind,
            Notification.Topic.CoppaCompliance
        };

        protected override SingleService serviceInstance => PurchasingService.instance;
        protected override string serviceUssClassName => "purchasing";

        protected override void ToggleRestrictedVisualElementsAvailability(bool enable)
        {
            var serviceToggleContainer = rootVisualElement.Q(className: k_ServiceToggleContainerClassName);
            var unityToggle = serviceToggleContainer?.Q(className: k_UnityToggleClassName);
            if (unityToggle != null)
            {
                unityToggle.SetEnabled(enable);
                if (!enable)
                {
                    var notifications = NotificationManager.instance.GetNotificationsForTopics(Notification.Topic.PurchasingService);
                    if (notifications.Any(notification => notification.rawMessage == k_PurchasingPermissionMessage))
                    {
                        return;
                    }

                    NotificationManager.instance.Publish(
                        Notification.Topic.PurchasingService,
                        Notification.Severity.Warning,
                        k_PurchasingPermissionMessage);
                }
            }
        }

        protected override void ActivateAction(string searchContext)
        {
            // Must reset properties every time this is activated
            var mainTemplate = EditorGUIUtility.Load(k_PurchasingServicesTemplatePath) as VisualTreeAsset;
            rootVisualElement.Add(mainTemplate.CloneTree().contentContainer);

            if (!PurchasingService.instance.IsServiceEnabled())
            {
                m_StateMachine.Initialize(m_DisabledState);
            }
            else
            {
                m_StateMachine.Initialize(m_EnabledState);
            }

            // Moved the Go to dashboard link to the header title section.
            m_GoToDashboard = rootVisualElement.Q(k_GoToDashboardLink);
            if (m_GoToDashboard != null)
            {
                var clickable = new Clickable(() =>
                {
                    if (CheckMinimumVersion(m_EnabledState.CurPackageVersion(), k_UnityAnalyticsSupportMajorVersion, k_UnityAnalyticsSupportMinorVersion))
                        ServicesConfiguration.instance.RequestBasePurchasingDashboardUrl(OpenDashboardForOrgKeyAndProjectGuid);
                    else
                        ServicesConfiguration.instance.RequestBaseLegacyPurchasingDashboardUrl(OpenDashboardForProjectGuid);
                });
                m_GoToDashboard.AddManipulator(clickable);
            }

            m_MainServiceToggle = rootVisualElement.Q<Toggle>(className: k_ServiceToggleClassName);
            SetupServiceToggle(PurchasingService.instance);

            InitializeServiceCallbacks();
        }

        void SetupServiceToggle(SingleService singleService)
        {
            m_MainServiceToggle.SetProperty(k_ServiceNameProperty, singleService.name);
            m_MainServiceToggle.SetEnabled(false);
            UpdateServiceToggleAndDashboardLink(singleService.IsServiceEnabled());

            if (singleService.displayToggle)
            {
                m_MainServiceToggle.RegisterValueChangedCallback(evt =>
                {
                    if (currentUserPermission != UserRole.Owner && currentUserPermission != UserRole.Manager)
                    {
                        UpdateServiceToggleAndDashboardLink(evt.previousValue);
                        return;
                    }
                    singleService.EnableService(evt.newValue);
                });
            }
            else
            {
                m_MainServiceToggle.style.display = DisplayStyle.None;
            }
        }

        void UpdateServiceToggleAndDashboardLink(bool isEnabled)
        {
            if (m_GoToDashboard != null)
            {
                m_GoToDashboard.style.display = (isEnabled) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (m_MainServiceToggle != null)
            {
                m_MainServiceToggle.SetValueWithoutNotify(isEnabled);
                SetupServiceToggleLabel(m_MainServiceToggle, isEnabled);
            }
        }

        protected override void DeactivateAction()
        {
            m_StateMachine.ClearCurrentState();

            FinalizeServiceCallbacks();
        }

        void InitializeServiceCallbacks()
        {
            if (!m_CallbacksInitialized)
            {
                //Bind states to external changes
                PurchasingService.instance.serviceAfterEnableEvent += EnableOperationCompleted;
                PurchasingService.instance.serviceAfterDisableEvent += DisableOperationCompleted;

                m_CallbacksInitialized = true;
            }
        }

        void FinalizeServiceCallbacks()
        {
            if (m_CallbacksInitialized)
            {
                //Bind states to external changes
                PurchasingService.instance.serviceAfterEnableEvent -= EnableOperationCompleted;
                PurchasingService.instance.serviceAfterDisableEvent -= DisableOperationCompleted;

                m_CallbacksInitialized = false;
            }
        }

        void EnableOperationCompleted(object sender, EventArgs args)
        {
            m_StateMachine.ProcessEvent(ServiceEvent.Enabled);
        }

        void DisableOperationCompleted(object sender, EventArgs args)
        {
            m_StateMachine.ProcessEvent(ServiceEvent.Disabled);
        }

        internal enum ServiceEvent
        {
            Disabled,
            Enabled,
        }

        static bool CheckMinimumVersion(string packageVersion, int majorVersion, int minorVersion)
        {
            string[] versionSplits = packageVersion.Split(new[] {'.'});
            if (versionSplits.Length < 2)
                return false;

            if (!int.TryParse(versionSplits[0], out var curMajorVersion) || curMajorVersion < majorVersion)
                return false;

            return curMajorVersion != majorVersion || (int.TryParse(versionSplits[1], out var curMinorVersion) && curMinorVersion >= minorVersion);
        }

        class BasePurchasingState : GenericBaseState<PurchasingProjectSettings, ServiceEvent>
        {
            readonly string[] k_PresumedSupportedStores =
            {
                "Amazon Appstore",
                "Facebook Gameroom",
                "Google Play",
                "iOS and tvOs App Store",
                "Mac App Store",
                "Samsung Galaxy Apps",
                "Unity Distribution Portal",
                "Windows Store"
            };

            //Common uss class names
            const string k_TagClass = "platform-tag";
            const string k_TagContainterClass = "tag-container";
            protected const string k_ScrollContainerClass = "scroll-container";

            //Common uxml element names
            protected const string k_SupportedPlatformsBlock = "SupportedPlatformsBlock";

            protected BasePurchasingState(string stateName, SimpleStateMachine<ServiceEvent> stateMachine, PurchasingProjectSettings provider)
                : base(stateName, stateMachine, provider)
            {
            }

            protected void LoadTemplateIntoScrollContainer(string templatePath)
            {
                var generalTemplate = EditorGUIUtility.Load(templatePath) as VisualTreeAsset;
                var rootElement = provider.rootVisualElement;
                if (rootElement != null)
                {
                    var scrollContainer = provider.rootVisualElement.Q(className: k_ScrollContainerClass);
                    scrollContainer.Clear();
                    scrollContainer.Add(generalTemplate.CloneTree().contentContainer);
                    ServicesUtils.TranslateStringsInTree(provider.rootVisualElement);
                }
            }

            protected List<string> GetSupportedPlatforms()
            {
                return k_PresumedSupportedStores.ToList();
            }
        }

        /// <summary>
        /// This state is active when the In-App Purchase package is not enabled
        /// </summary>
        sealed class DisabledState : BasePurchasingState
        {
            const string k_TemplatePath = "UXML/ServicesWindow/PurchasingProjectSettingsStateDisabled.uxml";

            public DisabledState(SimpleStateMachine<ServiceEvent> stateMachine, PurchasingProjectSettings provider)
                : base(k_StateNameDisabled, stateMachine, provider)
            {
                ModifyActionForEvent(ServiceEvent.Enabled, HandleEnabling);
            }

            public override void EnterState()
            {
                LoadTemplateIntoScrollContainer(k_TemplatePath);

                var scrollContainer = provider.rootVisualElement.Q(className: k_ScrollContainerClass);
                scrollContainer.Add(ServicesUtils.SetupSupportedPlatformsBlock(GetSupportedPlatforms()));

                provider.UpdateServiceToggleAndDashboardLink(provider.serviceInstance.IsServiceEnabled());

                provider.HandlePermissionRestrictedControls();

                UpdatePackageInformation();
            }

            SimpleStateMachine<ServiceEvent>.State HandleEnabling(ServiceEvent raisedEvent)
            {
                return stateMachine.GetStateByName(k_StateNameEnabled);
            }
        }

        /// <summary>
        /// This state is active when the In-App Purchase package is enabled
        /// This state will be derived from for various substates
        /// </summary>
        sealed class EnabledState : BasePurchasingState
        {
            const string k_TemplatePath = "UXML/ServicesWindow/PurchasingProjectSettingsStateEnabled.uxml";
            const string k_BulletCharacter = "\u2022 ";

            Label m_MigrationMessage;
            VisualElement m_ImportIapBlock;
            VisualElement m_IapOptionsBlock;

            //uss class names
            const string k_BulletClass = "bullet-item";
            const string k_BulletContainerClass = "bullet-container";

            //uxml element names
            const string k_MigrationMessage = "MigrateMessage";
            const string k_ImportIapBlock = "ImportIapBlock";
            const string k_IapOptionsBlock = "IapOptionsBlock";
            const string k_ImportBtn = "ImportBtn";
            const string k_ReimportBtn = "ReimportBtn";
            const string k_UpdateBtn = "UpdateBtn";
            const string k_GooglePlayLink = "GooglePlayLink";
            const string k_GoToDashboardLink = "GoToDashboard";
            const string k_DashboardSettingsLink = "DashboardSettingsLink";
            const string k_GooglePlayKeyText = "GooglePlayKey";
            

            //Package Import status blocks
            const string k_UnimportedMode = "unimported-mode";
            private const string k_VersionCheckMode = "version-check-mode";
            const string k_UpToDataMode = "up-to-date-mode";
            const string k_OutOfDateMode = "out-of-date-mode";

            //Google Play Key status blocks
            const string k_VerifiedMode = "verified-mode";
            const string k_UnverifiedMode = "unverified-mode";
            const string k_ErrorKeyFormat = "error-key-format";
            const string k_ErrorUnauthorized = "error-unauthorized-user";
            const string k_ErrorServer = "error-server-error";
            const string k_ErrorFethcKey = "error-fetch-key";

            //Popup Messages
            const string k_PackageMigrationHeadsup = "You are about to migrate to the more modern {0}. This will modify your project files. Be sure to make a backup first.\nDo you want to continue?";

            string packageMigrationHeadsup { get; set; }
            bool m_LookForAssetStoreImport;
            bool m_EligibleForMigration;

            string m_GooglePlayKey;

            enum ImportState
            {
                Unimported,
                VersionCheck,
                UpToDate,
                OutOfDate
            }

            enum GooglePlayKeyState
            {
                Verified,
                InvalidFormat,
                UnauthorizedUser,
                ServerError,
                CantFetch
            }

            GooglePlayKeyState m_GooglePlayKeyState;

            public EnabledState(SimpleStateMachine<ServiceEvent> stateMachine, PurchasingProjectSettings provider)
                : base(k_StateNameEnabled, stateMachine, provider)
            {
                topicForNotifications = Notification.Topic.PurchasingService;
                notLatestPackageInstalledInfo = string.Format(k_NotLatestPackageInstalledInfo, k_PurchasingPackageName);
                packageInstallationHeadsup = string.Format(k_PackageInstallationHeadsup, k_PurchasingPackageName);
                packageMigrationHeadsup = string.Format(k_PackageMigrationHeadsup, k_PurchasingPackageName);
                duplicateInstallWarning = null;
                packageInstallationDialogTitle = string.Format(k_PackageInstallationDialogTitle, k_PurchasingPackageName);

                ModifyActionForEvent(ServiceEvent.Disabled, HandleDisabling);
            }

            public override void EnterState()
            {
                LoadTemplateIntoScrollContainer(k_TemplatePath);

                m_MigrationMessage = provider.rootVisualElement.Q<Label>(k_MigrationMessage);
                m_ImportIapBlock = provider.rootVisualElement.Q(k_ImportIapBlock);
                m_IapOptionsBlock = provider.rootVisualElement.Q(k_IapOptionsBlock);

                ToggleMigrateModeVisibility(m_MigrationMessage, m_EligibleForMigration);

                SetupWelcomeIapBlock();
                SetupImportIapBlock();
                SetupIapOptionsBlock();
                var scrollContainer = provider.rootVisualElement.Q(className: k_ScrollContainerClass);
                scrollContainer.Add(ServicesUtils.SetupSupportedPlatformsBlock(GetSupportedPlatforms()));

                provider.UpdateServiceToggleAndDashboardLink(provider.serviceInstance.IsServiceEnabled());

                provider.HandlePermissionRestrictedControls();

                // Prepare the package section and update the package information
                PreparePackageSection(provider.rootVisualElement);
                UpdatePackageInformation();
            }

            void SetupWelcomeIapBlock()
            {
                var platforms = GetSupportedPlatforms();

                VisualElement welcomeToIapBlock = provider.rootVisualElement.Q(k_WelcomeToIapBlock);
                var bulletContainer = welcomeToIapBlock.Q(className: k_BulletContainerClass);
                bulletContainer.Clear();

                foreach (var platform in platforms)
                {
                    var tag = new Label(k_BulletCharacter + platform);
                    tag.AddToClassList(k_BulletClass);
                    bulletContainer.Add(tag);
                }
            }

            //Begin Import Block
            void SetupImportIapBlock()
            {
                Action requestLambda = () =>
                {
                    RequestImportOperation();
                };

                m_ImportIapBlock.Q<Button>(k_ImportBtn).clicked += requestLambda;
                m_ImportIapBlock.Q<Button>(k_ReimportBtn).clicked += requestLambda;
                m_ImportIapBlock.Q<Button>(k_UpdateBtn).clicked += requestLambda;

                VerifyImportTag();

                // The check for a newer version should be done only when the service is enabled, and at least once when entering the state ...
                PurchasingService.instance.RequestNotifyOnVersionCheck(OnVersionCheckComplete);
                PurchasingService.instance.GetLatestETag(PurchasingService.instance.OnGetLatestETag);
            }

            void VerifyImportTag()
            {
                string importTag = PurchasingService.instance.GetInstalledETag();
                ImportState importState;

                if (importTag == null)
                {
                    importState = ImportState.Unimported;
                }
                else if (PurchasingService.instance.latestETag == PurchasingService.instance.waitingOnPackage)
                {
                    importState = ImportState.VersionCheck;
                }
                else if (importTag != PurchasingService.instance.unknownPackage)
                {
                    importState = (importTag == PurchasingService.instance.latestETag) ? ImportState.UpToDate : ImportState.OutOfDate;
                }
                else
                {
                    importState = ImportState.OutOfDate;
                }

                ToggleImportModeVisibility(m_ImportIapBlock, importState, m_LookForAssetStoreImport);

                if (importState == ImportState.VersionCheck)
                {
                    PurchasingService.instance.RequestNotifyOnVersionCheck(OnVersionCheckComplete);
                }
            }

            void ToggleImportModeVisibility(VisualElement fieldBlock, ImportState importState, bool canImport)
            {
                if (fieldBlock != null)
                {
                    if (canImport)
                    {
                        fieldBlock.style.display = DisplayStyle.Flex;

                        var unimportedMode = fieldBlock.Q(k_UnimportedMode);
                        if (unimportedMode != null)
                        {
                            unimportedMode.style.display = (importState == ImportState.Unimported)
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        var versionCheckMode = fieldBlock.Q(k_VersionCheckMode);
                        if (versionCheckMode != null)
                        {
                            versionCheckMode.style.display = (importState == ImportState.VersionCheck)
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        var upToDateMode = fieldBlock.Q(k_UpToDataMode);
                        if (upToDateMode != null)
                        {
                            upToDateMode.style.display = (importState == ImportState.UpToDate)
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        var outOfDateMode = fieldBlock.Q(k_OutOfDateMode);
                        if (outOfDateMode != null)
                        {
                            outOfDateMode.style.display = (importState == ImportState.OutOfDate)
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }
                    }
                    else
                    {
                        fieldBlock.style.display = DisplayStyle.None;
                    }
                }
            }

            void ToggleMigrateModeVisibility(VisualElement fieldBlock, bool canMigrate)
            {
                if (fieldBlock != null)
                {
                    fieldBlock.style.display = canMigrate ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            protected override void OnSearchPackageFound(PackageManager.PackageInfo package)
            {
                base.OnSearchPackageFound(package);

                m_EligibleForMigration = false;
                if (TryGetMajorVersion(currentPackageVersion, out var currentMajorVer))
                {
                    if (currentMajorVer <= 2)
                    {
                        if (TryGetMajorVersion(latestPackageVersion, out var majorVer))
                        {
                            if (majorVer >= 3)
                            {
                                m_EligibleForMigration = true;
                            }
                        }
                    }
                }
            }

            protected override void PackageInformationUpdated()
            {
                m_LookForAssetStoreImport = false;

                if (packmanPackageInstalled && TryGetMajorVersion(currentPackageVersion, out var currentMajorVer))
                {
                    if (currentMajorVer <= 2)
                    {
                        m_LookForAssetStoreImport = true;
                    }
                }

                VerifyImportTag();
                ToggleMigrateModeVisibility(m_MigrationMessage, m_EligibleForMigration);
            }

            bool TryGetMajorVersion(string versionName, out int majorVersion)
            {
                return int.TryParse(versionName.Split('.')[0], out majorVersion);
            }

            bool IsPreviewVersion(string versionName)
            {
                var previewTag = "preview"; //TODO: update to an array if more nomenclature exists.
                return versionName.Contains(previewTag);
            }

            void RequestImportOperation()
            {
                PurchasingService.instance.InstallUnityPackage(OnImportComplete);
            }

            protected override string GetUpdatePackageMessage()
            {
                if (m_EligibleForMigration)
                {
                    return L10n.Tr(packageMigrationHeadsup);
                }
                else
                {
                    return base.GetUpdatePackageMessage();
                }
            }

            void OnImportComplete()
            {
                VerifyImportTag();
            }

            void OnVersionCheckComplete()
            {
                VerifyImportTag();
            }

            //End Import Block

            //Begin Options Block
            void SetupIapOptionsBlock()
            {
                RequestRetrieveOperation();

                m_IapOptionsBlock.Q<Button>(k_GooglePlayLink).clicked += () =>
                {
                    Application.OpenURL(PurchasingConfiguration.instance.googlePlayDevConsoleUrl);
                };

                var projectSettingsDashboardLink = m_IapOptionsBlock.Q(k_DashboardSettingsLink);
                if (projectSettingsDashboardLink != null)
                {
                    var clickable = new Clickable(OpenProjectSettingsUnityDashboard);
                    projectSettingsDashboardLink.AddManipulator(clickable);
                }
            }

            static void OpenProjectSettingsUnityDashboard()
            {
                Application.OpenURL(BuildProjectSettingsUri());
            }

            static string BuildProjectSettingsUri()
            {
                return string.Format(PurchasingConfiguration.k_ProjectSettingsUrl, UnityConnect.instance.GetOrganizationForeignKey(), CloudProjectSettings.projectId);
            }

            void ToggleGoogleKeyStateVisibility(VisualElement fieldBlock, GooglePlayKeyState importState)
            {
                if (fieldBlock != null)
                {
                    var unVerifiedMode = fieldBlock.Q(k_UnverifiedMode);
                    if (unVerifiedMode != null)
                    {
                        unVerifiedMode.style.display = (importState == GooglePlayKeyState.Verified)
                            ? DisplayStyle.None
                            : DisplayStyle.Flex;

                        var badFormatError = fieldBlock.Q(k_ErrorKeyFormat);
                        if (badFormatError != null)
                        {
                            badFormatError.style.display = (importState == GooglePlayKeyState.InvalidFormat)
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        var unauthUserError = fieldBlock.Q(k_ErrorUnauthorized);
                        if (unauthUserError != null)
                        {
                            unauthUserError.style.display = (importState == GooglePlayKeyState.UnauthorizedUser)
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        var serverError = fieldBlock.Q(k_ErrorServer);
                        if (serverError != null)
                        {
                            serverError.style.display = (importState == GooglePlayKeyState.ServerError)
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        var cantFetchError = fieldBlock.Q(k_ErrorFethcKey);
                        if (cantFetchError != null)
                        {
                            cantFetchError.style.display = (importState == GooglePlayKeyState.CantFetch)
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }
                    }
                }
            }

            void RequestRetrieveOperation()
            {
                RetrieveGooglePlayKey();
            }

            void RetrieveGooglePlayKey()
            {
                PurchasingService.instance.GetGooglePlayKey(OnGetGooglePlayKey);
            }

            void OnGetGooglePlayKey(string googlePlayKey, long responseCode)
            {
                m_GooglePlayKey = googlePlayKey;
                m_GooglePlayKeyState = InterpretKeyStateFromProtocolError(responseCode);

                if (!string.IsNullOrEmpty(m_GooglePlayKey))
                {
                    m_IapOptionsBlock.Q<TextElement>(k_GooglePlayKeyText).text = m_GooglePlayKey;
                }

                ToggleGoogleKeyStateVisibility(m_IapOptionsBlock, m_GooglePlayKeyState);
            }

            static GooglePlayKeyState InterpretKeyStateFromProtocolError(long responseCode)
            {
                switch (responseCode)
                {
                    case 200:
                        return GooglePlayKeyState.Verified;
                    case 401:
                    case 403:
                        return GooglePlayKeyState.UnauthorizedUser;
                    case 400:
                    case 404:
                        return GooglePlayKeyState.CantFetch;
                    case 405:
                    case 500:
                    case -1:
                        return GooglePlayKeyState.ServerError;
                    default:
                        return GooglePlayKeyState.CantFetch; //Could instead use a generic unknown message, but this is good enough.
                }
            }

            //End Options Block

            SimpleStateMachine<ServiceEvent>.State HandleDisabling(ServiceEvent raisedEvent)
            {
                return stateMachine.GetStateByName(k_StateNameDisabled);
            }

            internal string CurPackageVersion()
            {
                return currentPackageVersion;
            }
        }
    }
}
