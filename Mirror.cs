using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteInEditMode]
public class Mirror : MonoBehaviour
{
    public GameObject thisGameObject => mrObj;
    private GameObject mrObj;
    private GameObject camObj;
    private Camera linkedCam;
    private RTHandle rt;
    private RTHandle rt2;
    private MeshRenderer mr;
    [Min(0.001f)]
    public float renderScale = 1f;
    public Vector2Int renderSize = Vector2Int.zero;
    private float oldRenderScale = 1f;
    private Vector2Int oldRenderSize = Vector2Int.zero;
    public bool excludeSceneCam = true;
    private Material mat;

    private void Awake()
    {
        mr = GetComponent<MeshRenderer>();
    }

    private void OnEnable()
    {
        if (mrObj == null)
            mrObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        mrObj.name = name + "_Quad";
        mrObj.transform.SetParent(transform, false);
        mrObj.hideFlags = HideFlags.HideAndDontSave;
        mr = mrObj.GetComponent<MeshRenderer>();
        if (camObj == null)
            camObj = new GameObject(name + "_Camera", typeof(Camera));
        camObj.transform.SetParent(transform, false);
        camObj.hideFlags = HideFlags.HideAndDontSave;
        linkedCam = camObj.GetComponent<Camera>();
        linkedCam.enabled = false;
        if (mat == null)
            mat = new Material(Shader.Find("VirtualBrightPlayz/Mirror"));
        mr.sharedMaterial = mat;
        RTHandles.Initialize(Screen.width, Screen.height);
        if (renderSize.x <= 0 || renderSize.y <= 0)
        {
            rt = RTHandles.Alloc(Vector2.one * renderScale, name: $"{name}_Main");
            rt2 = RTHandles.Alloc(Vector2.one * renderScale, name: $"{name}_Alt");
        }
        else
        {
            rt = RTHandles.Alloc(renderSize.x, renderSize.y, name: $"{name}_Main");
            rt2 = RTHandles.Alloc(renderSize.x, renderSize.y, name: $"{name}_Alt");
        }
        oldRenderScale = renderScale;
        oldRenderSize = renderSize;
        RenderPipelineManager.beginCameraRendering += OnCameraRender;
    }

    // https://forum.unity.com/threads/camera-current-returns-null-when-calling-it-in-onwillrenderobject-with-universalrp.929880/
    private static Plane[] frustrumPlanes = new Plane[6];

    private static bool IsVisible(Camera camera, Bounds bounds)
    {
        GeometryUtility.CalculateFrustumPlanes(camera, frustrumPlanes);
        return GeometryUtility.TestPlanesAABB(frustrumPlanes, bounds);
    }

    private void OnCameraRender(ScriptableRenderContext arg1, Camera arg2)
    {
        if (IsVisible(arg2, mr.bounds) && arg2.cameraType == CameraType.SceneView)
        {
            OnWillRenderObjectWCam(arg2, true);
        }
    }

    private void LateUpdate()
    {
        if (oldRenderScale != renderScale || oldRenderSize != renderSize)
        {
            OnDisable();
            OnEnable();
        }
        foreach (var cam in Camera.allCameras)
        {
            if (IsVisible(cam, mr.bounds))
                OnWillRenderObjectWCam(cam, true);
        }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnCameraRender;
        RTHandles.Release(rt);
        rt = null;
        RTHandles.Release(rt2);
        rt2 = null;
    }

    private void OnDestroy()
    {
        DestroyImmediate(mrObj);
        DestroyImmediate(camObj);
    }

    private void OnWillRenderObjectWCam(Camera current, bool render)
    {
        if (current == linkedCam)
            return;
        if (current.cameraType == CameraType.SceneView && excludeSceneCam)
            return;
        if (linkedCam && thisGameObject)
        {
            if (!render)
                return;

            if (current.stereoEnabled)
            {
                var pos = current.transform.position;
                var rot = current.transform.rotation;
                var pos2 = current.transform.position;
                var rot2 = current.transform.rotation;

                var mat = current.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
                var mat3 = current.GetStereoViewMatrix(Camera.StereoscopicEye.Right);

                var mat2 = current.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                var mat4 = current.GetStereoViewMatrix(Camera.StereoscopicEye.Left);

                RenderCam(current, pos, rot, mat, rt, "_MainTex");
                RenderCam(current, pos2, rot2, mat2, rt2, "_AltTex");
            }
            else
            {
                var pos = current.transform.position;
                var rot = current.transform.rotation;
                var mat = current.projectionMatrix;

                RenderCam(current, pos, rot, mat, rt, "_MainTex");
            }
        }
    }

    public void RenderCam(Camera current, Vector3 pos, Quaternion rot, Matrix4x4 mat, RTHandle rt3, string texId)
    {
        if (rt3 == null)
            return;
        linkedCam.stereoTargetEye = StereoTargetEyeMask.None;

        var prevRenderMat = linkedCam.projectionMatrix;
        var prevRenderNear = linkedCam.nearClipPlane;
        var prevPos = linkedCam.transform.position;
        var prevRot = linkedCam.transform.rotation;

        linkedCam.fieldOfView = current.fieldOfView;
        linkedCam.nearClipPlane = current.nearClipPlane;
        linkedCam.farClipPlane = current.farClipPlane;
        linkedCam.projectionMatrix = mat;

        linkedCam.transform.localPosition = Vector3.Reflect(thisGameObject.transform.InverseTransformPoint(pos), Vector3.forward);
        linkedCam.transform.localRotation = Quaternion.LookRotation(Vector3.Reflect(thisGameObject.transform.InverseTransformDirection(rot * Vector3.forward), Vector3.forward), Vector3.Reflect(thisGameObject.transform.InverseTransformDirection(rot * Vector3.up), Vector3.forward));

        Transform clipPlane = thisGameObject.transform;
        int dot = System.Math.Sign(Vector3.Dot(clipPlane.forward, (clipPlane.position - linkedCam.transform.position)));

        Vector3 camSpacePos = (linkedCam.worldToCameraMatrix).MultiplyPoint(clipPlane.position);
        Vector3 camSpaceNormal = (linkedCam.worldToCameraMatrix).MultiplyVector(clipPlane.forward) * dot;
        float camSpaceDist = -Vector3.Dot(camSpacePos, camSpaceNormal);
        Vector4 clipPlaneCamSpace = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDist);
        var renderMat = linkedCam.CalculateObliqueMatrix(clipPlaneCamSpace);
        linkedCam.projectionMatrix = renderMat;
        linkedCam.nearClipPlane = Vector3.Distance(linkedCam.transform.position, clipPlane.position);

        linkedCam.forceIntoRenderTexture = true;
        var oldrt = linkedCam.targetTexture;
        linkedCam.targetTexture = rt3;

        RenderPipeline.SubmitRenderRequest(linkedCam, new UniversalRenderPipeline.SingleCameraRequest()
        {
            destination = rt3,
        });
        linkedCam.targetTexture = oldrt;

        this.mat.SetTexture(texId, rt3);
    }
}
