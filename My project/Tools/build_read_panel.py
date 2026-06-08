from pathlib import Path

UI_PREFAB = Path(r"c:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Prefabs\UI\_UI.prefab")
LETTER_PREFAB = Path(r"c:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Prefabs\Environment\Letter.prefab")

FONT = "fca6355ee4a564c4ca900e2eeb4a9ad6"
READ_PANEL_SCRIPT = "e4f5a6b7c8d940618273645564738495"
READABLE_LETTER_SCRIPT = "f5a6b7c8d9e0417293847564758695a0"
CANVAS_RT = "5139216045131410393"
CANVAS_CHILD_MARKER = "  - {fileID: 6340261573679706532}"

UI_BLOCK = r"""
--- !u!1 &990001001
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 990001002}
  - component: {fileID: 990001003}
  m_Layer: 5
  m_Name: Read Panel
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!224 &990001002
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 990001001}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {fileID: 990001012}
  - {fileID: 990001022}
  m_Father: {fileID: 5139216045131410393}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
  m_AnchorMin: {x: 0, y: 0}
  m_AnchorMax: {x: 1, y: 1}
  m_AnchoredPosition: {x: 0, y: 0}
  m_SizeDelta: {x: 0, y: 0}
  m_Pivot: {x: 0.5, y: 0.5}
--- !u!114 &990001003
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 990001001}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: e4f5a6b7c8d940618273645564738495, type: 3}
  m_Name: 
  m_EditorClassIdentifier: 
  panelRoot: {fileID: 990001011}
  bodyText: {fileID: 990001024}
--- !u!1 &990001011
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 990001012}
  - component: {fileID: 990001013}
  - component: {fileID: 990001014}
  m_Layer: 5
  m_Name: Panel Root
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 0
--- !u!224 &990001012
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 990001011}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {fileID: 990001032}
  m_Father: {fileID: 990001002}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
  m_AnchorMin: {x: 0, y: 0}
  m_AnchorMax: {x: 1, y: 1}
  m_AnchoredPosition: {x: 0, y: 0}
  m_SizeDelta: {x: 0, y: 0}
  m_Pivot: {x: 0.5, y: 0.5}
--- !u!222 &990001013
CanvasRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 990001011}
  m_CullTransparentMesh: 1
--- !u!114 &990001014
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 990001011}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: fe87c0e1cc204ed48ad3b37840f39efc, type: 3}
  m_Name: 
  m_EditorClassIdentifier: 
  m_Material: {fileID: 0}
  m_Color: {r: 0, g: 0, b: 0, a: 0.62}
  m_RaycastTarget: 0
  m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}
  m_Maskable: 1
  m_OnCullStateChanged:
    m_PersistentCalls:
      m_Calls: []
  m_Sprite: {fileID: 10907, guid: 0000000000000000f000000000000000, type: 0}
  m_Type: 1
  m_PreserveAspect: 0
  m_FillCenter: 1
  m_FillMethod: 4
  m_FillAmount: 1
  m_FillClockwise: 1
  m_FillOrigin: 0
  m_UseSpriteMesh: 0
  m_PixelsPerUnitMultiplier: 1
--- !u!1 &990001021
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 990001022}
  - component: {fileID: 990001023}
  - component: {fileID: 990001024}
  m_Layer: 5
  m_Name: Body Text
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!224 &990001022
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 990001021}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 990001002}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
  m_AnchorMin: {x: 0.5, y: 0.5}
  m_AnchorMax: {x: 0.5, y: 0.5}
  m_AnchoredPosition: {x: 0, y: 0}
  m_SizeDelta: {x: 720, y: 420}
  m_Pivot: {x: 0.5, y: 0.5}
--- !u!222 &990001023
CanvasRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 990001021}
  m_CullTransparentMesh: 1
--- !u!114 &990001024
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 990001021}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: f4688fdb7df04437aeb418b961361dc5, type: 3}
  m_Name: 
  m_EditorClassIdentifier: 
  m_Material: {fileID: 0}
  m_Color: {r: 0.92, g: 0.9, b: 0.85, a: 1}
  m_RaycastTarget: 0
  m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}
  m_Maskable: 1
  m_OnCullStateChanged:
    m_PersistentCalls:
      m_Calls: []
  m_text: 
  m_isRightToLeft: 0
  m_fontAsset: {fileID: 11400000, guid: fca6355ee4a564c4ca900e2eeb4a9ad6, type: 2}
  m_sharedMaterial: {fileID: 7620476336498253322, guid: fca6355ee4a564c4ca900e2eeb4a9ad6, type: 2}
  m_fontSharedMaterials: []
  m_fontMaterial: {fileID: 0}
  m_fontMaterials: []
  m_fontColor32:
    serializedVersion: 2
    rgba: 4289967026
  m_fontColor: {r: 0.92, g: 0.9, b: 0.85, a: 1}
  m_enableVertexGradient: 0
  m_colorMode: 3
  m_fontColorGradient:
    topLeft: {r: 1, g: 1, b: 1, a: 1}
    topRight: {r: 1, g: 1, b: 1, a: 1}
    bottomLeft: {r: 1, g: 1, b: 1, a: 1}
    bottomRight: {r: 1, g: 1, b: 1, a: 1}
  m_fontColorGradientPreset: {fileID: 0}
  m_spriteAsset: {fileID: 0}
  m_tintAllSprites: 0
  m_StyleSheet: {fileID: 0}
  m_TextStyleHashCode: -1183493901
  m_overrideHtmlColors: 0
  m_faceColor:
    serializedVersion: 2
    rgba: 4294967295
  m_fontSize: 22
  m_fontSizeBase: 22
  m_fontWeight: 400
  m_enableAutoSizing: 0
  m_fontSizeMin: 18
  m_fontSizeMax: 72
  m_fontStyle: 0
  m_HorizontalAlignment: 2
  m_VerticalAlignment: 512
  m_textAlignment: 65535
  m_characterSpacing: 0
  m_wordSpacing: 0
  m_lineSpacing: 4
  m_lineSpacingMax: 0
  m_paragraphSpacing: 0
  m_charWidthMaxAdj: 0
  m_TextWrappingMode: 1
  m_wordWrappingRatios: 0.4
  m_overflowMode: 0
  m_linkedTextComponent: {fileID: 0}
  parentLinkedComponent: {fileID: 0}
  m_enableKerning: 0
  m_ActiveFontFeatures: 6e72656b
  m_enableExtraPadding: 0
  checkPaddingRequired: 0
  m_isRichText: 1
  m_EmojiFallbackSupport: 1
  m_parseCtrlCharacters: 1
  m_isOrthographic: 1
  m_isCullingEnabled: 0
  m_horizontalMapping: 0
  m_verticalMapping: 0
  m_uvLineOffset: 0
  m_geometrySortingOrder: 0
  m_IsTextObjectScaleStatic: 0
  m_VertexBufferAutoSizeReduction: 0
  m_useMaxVisibleDescender: 1
  m_pageToDisplay: 1
  m_margin: {x: 24, y: 24, z: 24, w: 24}
  m_isUsingLegacyAnimationComponent: 0
  m_isVolumetricText: 0
  m_hasFontAssetChanged: 0
  m_baseMaterial: {fileID: 0}
  m_maskOffset: {x: 0, y: 0, z: 0, w: 0}
"""

# Fix hierarchy: Body Text should be child of Panel Root, not Read Panel root
UI_BLOCK = UI_BLOCK.replace(
    "  m_Father: {fileID: 990001002}\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n  m_AnchorMin: {x: 0.5, y: 0.5}\n  m_AnchorMax: {x: 0.5, y: 0.5}\n  m_AnchoredPosition: {x: 0, y: 0}\n  m_SizeDelta: {x: 720, y: 420}",
    "  m_Father: {fileID: 990001012}\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n  m_AnchorMin: {x: 0.5, y: 0.5}\n  m_AnchorMax: {x: 0.5, y: 0.5}\n  m_AnchoredPosition: {x: 0, y: 0}\n  m_SizeDelta: {x: 720, y: 420}",
)
UI_BLOCK = UI_BLOCK.replace(
    "  m_Children:\n  - {fileID: 990001012}\n  - {fileID: 990001022}",
    "  m_Children:\n  - {fileID: 990001012}",
)
UI_BLOCK = UI_BLOCK.replace(
    "  m_Children:\n  - {fileID: 990001032}",
    "  m_Children:\n  - {fileID: 990001022}",
)
# remove unused Text Anchor object from output - actually already repurposed children list

text = UI_PREFAB.read_text(encoding="utf-8")
if "m_Name: Read Panel" not in text:
    text = text.replace(
        CANVAS_CHILD_MARKER,
        CANVAS_CHILD_MARKER + "\n  - {fileID: 990001002}",
        1,
    )
    text = text.rstrip() + "\n" + UI_BLOCK
    UI_PREFAB.write_text(text, encoding="utf-8")
    print("Added Read Panel to _UI.prefab")
else:
    print("Read Panel already in _UI.prefab")

letter = LETTER_PREFAB.read_text(encoding="utf-8")
if "ReadableLetter" not in letter and "f5a6b7c8d9e0417293847564758695a0" not in letter:
    letter = letter.replace(
        "    m_AddedComponents: []",
        """    m_AddedComponents:
    - targetCorrespondingSourceObject: {fileID: 919132149155446097, guid: 56720f4883419cc46bdfaf0e1cd58438, type: 3}
      insertIndex: -1
      addedObject: {fileID: 880010001}
    - targetCorrespondingSourceObject: {fileID: 919132149155446097, guid: 56720f4883419cc46bdfaf0e1cd58438, type: 3}
      insertIndex: -1
      addedObject: {fileID: 880010002}""",
        1,
    )
    letter = letter.rstrip() + """
--- !u!1 &880010000 stripped
GameObject:
  m_CorrespondingSourceObject: {fileID: 919132149155446097, guid: 56720f4883419cc46bdfaf0e1cd58438, type: 3}
  m_PrefabInstance: {fileID: 7704946878295899821}
  m_PrefabAsset: {fileID: 0}
--- !u!114 &880010001
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 880010000}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: f5a6b7c8d9e0417293847564758695a0, type: 3}
  m_Name: 
  m_EditorClassIdentifier: 
  letterText: They left without a word. If you are reading this, do not stay.
  readOnce: 1
--- !u!65 &880010002
BoxCollider:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 880010000}
  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_IsTrigger: 0
  m_ProvidesContacts: 0
  m_Enabled: 1
  serializedVersion: 3
  m_Size: {x: 12, y: 1, y: 18}
  m_Center: {x: 0, y: 0, z: 0}
"""
    # fix typo in box collider size
    letter = letter.replace("m_Size: {x: 12, y: 1, y: 18}", "m_Size: {x: 12, y: 1, z: 18}")
    LETTER_PREFAB.write_text(letter, encoding="utf-8")
    print("Updated Letter.prefab")
else:
    print("Letter.prefab already has ReadableLetter")
