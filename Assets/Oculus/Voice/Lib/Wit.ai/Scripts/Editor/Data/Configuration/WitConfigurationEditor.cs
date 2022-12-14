﻿/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * This source code is licensed under the license found in the
 * LICENSE file in the root directory of this source tree.
 */

using System;
using System.IO;
using Facebook.WitAi.Configuration;
using Facebook.WitAi.Data.Configuration;
using Facebook.WitAi.Utilities;
using Meta.Conduit.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditorInternal;
using UnityEngine;

namespace Facebook.WitAi.Windows
{
    public class WitConfigurationEditor : Editor
    {
        // Tab IDs
        protected const string TAB_APPLICATION_ID = "application";
        protected const string TAB_INTENTS_ID = "intents";
        protected const string TAB_ENTITIES_ID = "entities";
        protected const string TAB_TRAITS_ID = "traits";

        private static ConduitStatistics _statistics;
        private static readonly AssemblyMiner AssemblyMiner = new(new WitParameterValidator());
        private static readonly ManifestGenerator ManifestGenerator = new(new AssemblyWalker(), AssemblyMiner);
        public bool drawHeader = true;
        private string _appID;
        private string _appName;
        private bool _foldout = true;
        private bool _initialized;
        private int _requestTab;
        private string _serverToken;
        private readonly string[] _tabIds = { TAB_APPLICATION_ID, TAB_INTENTS_ID, TAB_ENTITIES_ID, TAB_TRAITS_ID };
        private bool manifestAvailable;
        public WitConfiguration configuration { get; private set; }

        // Generate
        private static ConduitStatistics Statistics
        {
            get
            {
                if (_statistics == null) _statistics = new ConduitStatistics(new PersistenceLayer());
                return _statistics;
            }
        }

        public virtual Texture2D HeaderIcon => WitTexts.HeaderIcon;

        public virtual string HeaderUrl => WitTexts.GetAppURL(WitConfigurationUtility.GetAppID(configuration),
            WitTexts.WitAppEndpointType.Settings);

        public virtual string OpenButtonLabel => WitTexts.Texts.WitOpenButtonLabel;

        public void OnDisable()
        {
            Statistics.Persist();
        }

        public void Initialize()
        {
            // Refresh configuration & auth tokens
            configuration = target as WitConfiguration;

            // Get app server token
            _serverToken = WitAuthUtility.GetAppServerToken(configuration);
            if (CanConfigurationRefresh(configuration) && WitConfigurationUtility.IsServerTokenValid(_serverToken))
            {
                // Get client token if needed
                _appID = WitConfigurationUtility.GetAppID(configuration);
                if (string.IsNullOrEmpty(_appID))
                    configuration.SetServerToken(_serverToken);
                // Refresh additional data
                else
                    SafeRefresh();
            }
        }

        public override void OnInspectorGUI()
        {
            // Init if needed
            if (!_initialized || configuration != target)
            {
                Initialize();
                _initialized = true;
            }

            // Draw header
            if (drawHeader)
            {
                WitEditorUI.LayoutHeaderButton(HeaderIcon, HeaderUrl);
                GUILayout.Space(WitStyles.HeaderPaddingBottom);
                EditorGUI.indentLevel++;
            }

            // Layout content
            LayoutContent();

            // Undent
            if (drawHeader) EditorGUI.indentLevel--;
        }

        private void LayoutConduitContent()
        {
            var manifestPath = configuration.ManifestEditorPath;
            manifestAvailable = File.Exists(manifestPath);

            var useConduit = GUILayout.Toggle(configuration.useConduit, "Use Conduit (Beta)");
            if (configuration.useConduit != useConduit)
            {
                configuration.useConduit = useConduit;
                EditorUtility.SetDirty(configuration);
            }

            EditorGUI.BeginDisabledGroup(!configuration.useConduit);
            {
                EditorGUI.indentLevel++;
                GUILayout.Space(EditorGUI.indentLevel * WitStyles.ButtonMargin);
                {
                    GUILayout.BeginHorizontal();
                    if (WitEditorUI.LayoutTextButton(manifestAvailable ? "Update Manifest" : "Generate Manifest"))
                        GenerateManifest(configuration, configuration.openManifestOnGeneration);
                    GUI.enabled = manifestAvailable;
                    if (WitEditorUI.LayoutTextButton("Select Manifest") && manifestAvailable)
                        Selection.activeObject =
                            AssetDatabase.LoadAssetAtPath<TextAsset>(configuration.ManifestEditorPath);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    GUILayout.Space(WitStyles.ButtonMargin);
                    configuration.autoGenerateManifest =
                        GUILayout.Toggle(configuration.autoGenerateManifest, "Auto Generate");
                }
                EditorGUI.indentLevel--;
                GUILayout.TextField($"Manifests generated: {Statistics.SuccessfulGenerations}");
            }
            EditorGUI.EndDisabledGroup();
        }

        protected virtual void LayoutContent()
        {
            // Begin vertical box
            GUILayout.BeginVertical(EditorStyles.helpBox);

            // Check for app name/id update
            ReloadAppData();

            // Title Foldout
            GUILayout.BeginHorizontal();
            var foldoutText = WitTexts.Texts.ConfigurationHeaderLabel;
            if (!string.IsNullOrEmpty(_appName)) foldoutText = foldoutText + " - " + _appName;

            _foldout = WitEditorUI.LayoutFoldout(new GUIContent(foldoutText), _foldout);
            // Refresh button
            if (CanConfigurationRefresh(configuration))
            {
                if (string.IsNullOrEmpty(_appName))
                {
                    var isValid = WitConfigurationUtility.IsServerTokenValid(_serverToken);
                    GUI.enabled = isValid;
                    if (WitEditorUI.LayoutTextButton(WitTexts.Texts.ConfigurationRefreshButtonLabel))
                        ApplyServerToken(_serverToken);
                }
                else
                {
                    var isRefreshing = configuration.IsRefreshingData();
                    GUI.enabled = !isRefreshing;
                    if (WitEditorUI.LayoutTextButton(isRefreshing
                            ? WitTexts.Texts.ConfigurationRefreshingButtonLabel
                            : WitTexts.Texts.ConfigurationRefreshButtonLabel)) SafeRefresh();
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(WitStyles.ButtonMargin);

            // Show configuration app data
            if (_foldout)
            {
                // Indent
                EditorGUI.indentLevel++;

                // Server access token
                var updated = false;
                WitEditorUI.LayoutPasswordField(WitTexts.ConfigurationServerTokenContent, ref _serverToken,
                    ref updated);
                if (updated) ApplyServerToken(_serverToken);

                // Additional data
                if (configuration) LayoutConfigurationData();

                // Undent
                EditorGUI.indentLevel--;
            }

            // End vertical box layout
            GUILayout.EndVertical();

            GUILayout.BeginVertical(EditorStyles.helpBox);
            LayoutConduitContent();
            GUILayout.EndVertical();

            // Layout configuration request tabs
            LayoutConfigurationRequestTabs();

            // Additional open wit button
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(OpenButtonLabel, WitStyles.TextButton)) Application.OpenURL(HeaderUrl);
        }

        // Reload app data if needed
        private void ReloadAppData()
        {
            // Check for changes
            var checkName = "";
            var checkID = "";
            if (configuration != null && configuration.application != null)
            {
                checkName = configuration.application.name;
                checkID = configuration.application.id;
            }

            // Reset
            if (!string.Equals(_appName, checkName) || !string.Equals(_appID, checkID))
            {
                _appName = checkName;
                _appID = checkID;
                _serverToken = WitAuthUtility.GetAppServerToken(configuration);
            }
        }

        // Apply server token
        public void ApplyServerToken(string newToken)
        {
            _serverToken = newToken;
            configuration.ResetData();
            configuration.SetServerToken(_serverToken);
        }

        // Whether or not to allow a configuration to refresh
        protected virtual bool CanConfigurationRefresh(WitConfiguration configuration)
        {
            return configuration;
        }

        // Layout configuration data
        protected virtual void LayoutConfigurationData()
        {
            // Reset update
            var updated = false;
            // Client access field
            WitEditorUI.LayoutPasswordField(WitTexts.ConfigurationClientTokenContent,
                ref configuration.clientAccessToken, ref updated);
            if (updated && string.IsNullOrEmpty(configuration.clientAccessToken))
                Debug.LogError("Client access token is not defined. Cannot perform requests with '" +
                               configuration.name + "'.");
            // Timeout field
            WitEditorUI.LayoutIntField(WitTexts.ConfigurationRequestTimeoutContent, ref configuration.timeoutMS,
                ref updated);
            // Updated
            if (updated) EditorUtility.SetDirty(configuration);

            // Show configuration app data
            LayoutConfigurationEndpoint();
        }

        // Layout endpoint data
        protected virtual void LayoutConfigurationEndpoint()
        {
            // Generate if needed
            if (configuration.endpointConfiguration == null)
            {
                configuration.endpointConfiguration = new WitEndpointConfig();
                EditorUtility.SetDirty(configuration);
            }

            // Handle via serialized object
            var serializedObj = new SerializedObject(configuration);
            var serializedProp = serializedObj.FindProperty("endpointConfiguration");
            EditorGUILayout.PropertyField(serializedProp);
            serializedObj.ApplyModifiedProperties();
        }

        // Tabs
        protected virtual void LayoutConfigurationRequestTabs()
        {
            // Indent
            EditorGUI.indentLevel++;

            // Iterate tabs
            if (_tabIds != null)
            {
                GUILayout.BeginHorizontal();
                for (var i = 0; i < _tabIds.Length; i++)
                {
                    // Enable if not selected
                    GUI.enabled = _requestTab != i;
                    // If valid and clicked, begin selecting
                    var tabPropertyID = _tabIds[i];
                    if (ShouldTabShow(configuration, tabPropertyID))
                    {
                        if (WitEditorUI.LayoutTabButton(GetTabText(configuration, tabPropertyID, true)))
                            _requestTab = i;
                    }
                    // If invalid, stop selecting
                    else if (_requestTab == i)
                    {
                        _requestTab = -1;
                    }
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            // Layout selected tab using property id
            var propertyID = _requestTab >= 0 && _requestTab < _tabIds.Length ? _tabIds[_requestTab] : string.Empty;
            if (!string.IsNullOrEmpty(propertyID) && configuration != null)
            {
                var serializedObj = new SerializedObject(configuration);
                var serializedProp = serializedObj.FindProperty(propertyID);
                if (serializedProp == null)
                    WitEditorUI.LayoutErrorLabel(GetTabText(configuration, propertyID, false));
                else if (!serializedProp.isArray)
                    EditorGUILayout.PropertyField(serializedProp);
                else if (serializedProp.arraySize == 0)
                    WitEditorUI.LayoutErrorLabel(GetTabText(configuration, propertyID, false));
                else
                    for (var i = 0; i < serializedProp.arraySize; i++)
                    {
                        var serializedPropChild = serializedProp.GetArrayElementAtIndex(i);
                        EditorGUILayout.PropertyField(serializedPropChild);
                    }

                serializedObj.ApplyModifiedProperties();
            }

            // Undent
            EditorGUI.indentLevel--;
        }

        // Determine if tab should show
        protected virtual bool ShouldTabShow(WitConfiguration configuration, string tabID)
        {
            if (null == configuration.application ||
                string.IsNullOrEmpty(configuration.application.id))
                return false;

            switch (tabID)
            {
                case TAB_INTENTS_ID:
                    return null != configuration.intents;
                case TAB_ENTITIES_ID:
                    return null != configuration.entities;
                case TAB_TRAITS_ID:
                    return null != configuration.traits;
            }

            return true;
        }

        // Get tab text
        protected virtual string GetTabText(WitConfiguration configuration, string tabID, bool titleLabel)
        {
            switch (tabID)
            {
                case TAB_APPLICATION_ID:
                    return titleLabel
                        ? WitTexts.Texts.ConfigurationApplicationTabLabel
                        : WitTexts.Texts.ConfigurationApplicationMissingLabel;
                case TAB_INTENTS_ID:
                    return titleLabel
                        ? WitTexts.Texts.ConfigurationIntentsTabLabel
                        : WitTexts.Texts.ConfigurationIntentsMissingLabel;
                case TAB_ENTITIES_ID:
                    return titleLabel
                        ? WitTexts.Texts.ConfigurationEntitiesTabLabel
                        : WitTexts.Texts.ConfigurationEntitiesMissingLabel;
                case TAB_TRAITS_ID:
                    return titleLabel
                        ? WitTexts.Texts.ConfigurationTraitsTabLabel
                        : WitTexts.Texts.ConfigurationTraitsMissingLabel;
            }

            return string.Empty;
        }

        // Safe refresh
        protected virtual void SafeRefresh()
        {
            if (WitConfigurationUtility.IsServerTokenValid(_serverToken))
            {
                configuration.ResetData();
                configuration.SetServerToken(_serverToken);
            }
            else if (WitConfigurationUtility.IsClientTokenValid(configuration.clientAccessToken))
            {
                configuration.RefreshData();
            }
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            foreach (var witConfig in WitConfigurationUtility.WitConfigs)
                if (witConfig.useConduit && witConfig.autoGenerateManifest)
                    GenerateManifest(witConfig, false);
        }

        /// <summary>
        ///     Generates a manifest and optionally opens it in the editor.
        /// </summary>
        /// <param name="configuration">The configuration that we are generating the manifest for.</param>
        /// <param name="openManifest">If true, will open the manifest file in the code editor.</param>
        private static void GenerateManifest(WitConfiguration configuration, bool openManifest)
        {
            // Generate
            var startGenerationTime = DateTime.UtcNow;
            var manifest = ManifestGenerator.GenerateManifest(configuration.application.name,
                configuration.application.id);
            var endGenerationTime = DateTime.UtcNow;

            // Get file path
            var fullPath = configuration.ManifestEditorPath;
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                var directory = Application.dataPath + "/Oculus/Voice/Resources";
                IOUtility.CreateDirectory(directory);
                fullPath = directory + "/" + configuration.manifestLocalPath;
            }

            // Write to file
            try
            {
                var writer = new StreamWriter(fullPath);
                writer.WriteLine(manifest);
                writer.Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"Wit Configuration Editor - Conduit Manifest Creation Failed\nPath: {fullPath}\n{e}");
                return;
            }

            Statistics.SuccessfulGenerations++;
            Statistics.AddFrequencies(AssemblyMiner.SignatureFrequency);
            Statistics.AddIncompatibleFrequencies(AssemblyMiner.IncompatibleSignatureFrequency);
            var generationTime = endGenerationTime - startGenerationTime;
            AssetDatabase.ImportAsset(fullPath.Replace(Application.dataPath, "Assets"));

            Debug.Log($"Done generating manifest. Total time: {generationTime.TotalMilliseconds} ms");

            if (openManifest) InternalEditorUtility.OpenFileAtLineExternal(fullPath, 1);
        }
    }
}
