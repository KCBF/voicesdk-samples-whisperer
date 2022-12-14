﻿/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * This source code is licensed under the license found in the
 * LICENSE file in the root directory of this source tree.
 */

using Facebook.WitAi.Data.Configuration;
using UnityEditor;
using UnityEngine;

namespace Facebook.WitAi.Windows
{
    public class WitWindow : WitConfigurationWindow
    {
        protected string serverToken;
        protected WitConfigurationEditor witInspector;
        protected override GUIContent Title => WitTexts.SettingsTitleContent;
        protected override string HeaderUrl => witInspector ? witInspector.HeaderUrl : base.HeaderUrl;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (string.IsNullOrEmpty(serverToken)) serverToken = WitAuthUtility.ServerToken;
            SetWitEditor();
        }

        protected virtual void SetWitEditor()
        {
            if (witConfiguration)
            {
                witInspector = (WitConfigurationEditor)Editor.CreateEditor(witConfiguration);
                witInspector.drawHeader = false;
                witInspector.Initialize();
            }
            else if (witInspector != null)
            {
                DestroyImmediate(witInspector);
                witInspector = null;
            }
        }

        protected override void LayoutContent()
        {
            // Server access token
            GUILayout.BeginHorizontal();
            var updated = false;
            WitEditorUI.LayoutPasswordField(WitTexts.SettingsServerTokenContent, ref serverToken, ref updated);
            if (updated) RelinkServerToken(false);
            if (WitEditorUI.LayoutTextButton(WitTexts.Texts.SettingsRelinkButtonLabel)) RelinkServerToken(true);
            if (WitEditorUI.LayoutTextButton(WitTexts.Texts.SettingsAddButtonLabel))
            {
                var newIndex = WitConfigurationUtility.CreateConfiguration(serverToken);
                if (newIndex != -1) SetConfiguration(newIndex);
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(WitStyles.ButtonMargin);

            // Configuration select
            base.LayoutContent();
            // Update inspector if needed
            if (witInspector == null || witConfiguration == null || witInspector.configuration != witConfiguration)
                SetWitEditor();

            // Layout configuration inspector
            if (witConfiguration && witInspector) witInspector.OnInspectorGUI();
        }

        // Apply server token
        private void RelinkServerToken(bool closeIfInvalid)
        {
            // Open Setup if Invalid
            var invalid = !WitConfigurationUtility.IsServerTokenValid(serverToken);
            if (invalid)
            {
                // Clear if desired
                if (string.IsNullOrEmpty(serverToken)) WitAuthUtility.ServerToken = serverToken;
                // Close if desired
                if (closeIfInvalid)
                {
                    // Open Setup
                    WitWindowUtility.OpenSetupWindow(WitWindowUtility.OpenConfigurationWindow);
                    // Close this Window
                    Close();
                }

                return;
            }

            // Set valid server token
            WitAuthUtility.ServerToken = serverToken;
            WitConfigurationUtility.SetServerToken(serverToken);
        }
    }
}
