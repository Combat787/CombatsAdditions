using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using BepInEx;
using Rewired;
using Rewired.Data;
using UnityEngine;

namespace RewiredExtraMouse
{
    [BepInPlugin("com.rewired.mouseinjector", "Rewired Extra Mouse Buttons", "1.0.6")]
    public class RewiredMouseInjector : BaseUnityPlugin
    {
        private const int CUSTOM_CONTROLLER_ID = 8675309;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_XBUTTON1 = 0x05;
        private const int VK_XBUTTON2 = 0x06;

        private CustomController _virtualMouse;
        private bool _isInitialized = false;

        void Awake()
        {
            // Inject the controller definition early, before Rewired initializes.
            InjectCustomControllerDefinition();

            // Subscribe to the event fired when Rewired is ready.
            ReInput.InitializedEvent += OnRewiredInitialized;
        }

        private void InjectCustomControllerDefinition()
        {
            // --- Step 1: Find the list of definitions via Reflection ---
            FieldInfo configField = typeof(ReInput).GetField("_configuration", BindingFlags.Static | BindingFlags.NonPublic);
            if (configField == null) { Logger.LogError("Cannot find internal configuration field."); return; }

            object reInputConfigInstance = configField.GetValue(null);
            if (reInputConfigInstance == null) { Logger.LogWarning("ReInput config instance is null."); return; }

            FieldInfo customControllersField = reInputConfigInstance.GetType().GetField("customControllers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (customControllersField == null) { Logger.LogError("Cannot find customControllers field."); return; }

            var customControllers = customControllersField.GetValue(reInputConfigInstance) as List<CustomController_Editor>;
            if (customControllers == null) { Logger.LogError("Custom Controllers list is null."); return; }

            if (customControllers.Find(x => x.id == CUSTOM_CONTROLLER_ID) != null) { return; }

            Logger.LogInfo("Injecting 'Extra Mouse' Controller Definition via Full Reflection...");

            // --- Step 2: Create the CustomController_Editor object ---
            Type definitionType = typeof(CustomController_Editor);
            CustomController_Editor def = FormatterServices.GetUninitializedObject(definitionType) as CustomController_Editor;
            if (def == null) { Logger.LogError("Failed to create uninitialized definition object."); return; }

            // Set Read-Only Fields using Reflection
            FieldInfo idField = definitionType.GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? definitionType.GetField("<id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (idField != null) idField.SetValue(def, CUSTOM_CONTROLLER_ID);

            FieldInfo nameField = definitionType.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? definitionType.GetField("<name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (nameField != null) nameField.SetValue(def, "Extra Mouse Buttons");

            // --- Step 3: Discover and populate the element list ---
            FieldInfo elementsField = definitionType.GetField("elements", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (elementsField == null) { Logger.LogError("Cannot find the 'elements' field on CustomController_Editor."); return; }

            Type elementDefinitionType = elementsField.FieldType.GetGenericArguments()[0];

            List<object> newElements = new List<object>();

            Action<string, int> createAndAddElement = (elementName, elementId) =>
            {
                object element = FormatterServices.GetUninitializedObject(elementDefinitionType);

                // Set name
                FieldInfo elemNameField = elementDefinitionType.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? elementDefinitionType.GetField("<name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (elemNameField != null) elemNameField.SetValue(element, elementName);

                // Set elementIdentifierId
                FieldInfo idenfifierIdField = elementDefinitionType.GetField("elementIdentifierId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (idenfifierIdField != null) idenfifierIdField.SetValue(element, elementId);

                // Set elementType (using public Enum)
                FieldInfo typeField = elementDefinitionType.GetField("elementType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (typeField != null) typeField.SetValue(element, ControllerElementType.Button);

                // Set axisRange (using public Enum)
                FieldInfo rangeField = elementDefinitionType.GetField("axisRange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (rangeField != null) rangeField.SetValue(element, AxisRange.Positive);

                // Set invert
                FieldInfo invertField = elementDefinitionType.GetField("invert", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (invertField != null) invertField.SetValue(element, false);

                newElements.Add(element);
            };

            createAndAddElement("Mouse 4 (XButton1)", 0); // Element ID 0
            createAndAddElement("Mouse 5 (XButton2)", 1); // Element ID 1

            // 4. Assign the new list to the definition object's 'elements' field
            elementsField.SetValue(def, newElements);

            // 5. Add the final definition to the configuration list
            customControllers.Add(def);
            Logger.LogInfo("Injection complete. Definition added to the list.");
        }

        private void OnRewiredInitialized()
        {
            if (_isInitialized) return;

            // 1. Create the virtual controller from the definition we injected
            _virtualMouse = ReInput.controllers.CreateCustomController(CUSTOM_CONTROLLER_ID);

            if (_virtualMouse != null)
            {
                Logger.LogInfo("Virtual Mouse Controller Created!");

                // 2. Assign this new controller to all current Players
                foreach (var player in ReInput.players.Players)
                {
                    player.controllers.AddController(_virtualMouse, false);
                }

                // *** REMOVED: ReInput.players.PlayerAddedEvent subscription ***

                _isInitialized = true;
            }
            else
            {
                Logger.LogError("Failed to create Custom Controller.");
            }
        }

        void Update()
        {
            if (!_isInitialized || _virtualMouse == null) return;

            // Read Physical Hardware
            bool m4 = (GetAsyncKeyState(VK_XBUTTON1) & 0x8000) != 0;
            bool m5 = (GetAsyncKeyState(VK_XBUTTON2) & 0x8000) != 0;

            // Feed the Virtual Controller
            _virtualMouse.SetButtonValueById(0, m4);
            _virtualMouse.SetButtonValueById(1, m5);
        }
    }
}