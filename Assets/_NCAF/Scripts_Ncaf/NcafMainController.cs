﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Linq;
using NcCommon;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Collections;

namespace NcAF
{
    public enum AlIGNMODE { MANUAL, IMAGE_ONLY, TOUCH }
    public enum IMAGEALIGNMODE { SINGLE, INTEPOLATION }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(ARReferencePointManager))]
    [RequireComponent(typeof(ARPlaneManager))]
    //[RequireComponent(typeof(ARTrackedImageManager))]
    [RequireComponent(typeof(NcafUIController))]



    public class NcafMainController : MonoBehaviour
    {
        #region privateMembers
        // singleton stuff
        static NcafMainController _instance;

        // book keeping vars
        Dictionary<string, NcafARImageInfo> m_ARImageInfoDict = new Dictionary<string, NcafARImageInfo>();
        public Dictionary<string, NcafARImageInfo> m_ARImageInfos { get => m_ARImageInfoDict; }

        //Dictionary<TrackableId, ARPlane> m_allDetectedARPlanes = new Dictionary<TrackableId, ARPlane>();
        //Dictionary<TrackableId, ARPlane> m_curretlyDetectedARPlanes = new Dictionary<TrackableId, ARPlane>();

        IEnumerator m_worldTrackingAlignProcess = null;

        // AR foundtion managers
        ARPlaneManager m_ARPlaneManager;
        ARTrackedImageManager m_ARImageManager;
        ARReferencePointManager m_ReferencePointManager;

        // Book keeping vars
        ARTrackedImage latestTrackedARImage = null;
        float m_distToTrackedImage = 9999999f;
        ARPlane m_latestRefARPlane = null;
        ARReferencePoint m_currentRefPoint = null;
        ReferenceTracker m_refTracker { get; set; }

        // for sanitycheck in the first frame
        bool m_isSanityChecked = false;
        bool m_isImageManagerAvailable = false;

        #endregion


        #region publicMemebers
        // Main Controller Singleton Instance
        public static NcafMainController Instance { get { return _instance; } }
        [Header("General App Setting")]
        public int m_timeoutAfterTrackingIsLost = 30; // screen dim-out
        public float m_alignInterpolationTimer = 2f;

        [Header("AR Foundation Params")]
        public ARSession m_arSession;

        [Header("Exhibition Contents")]
        public Camera m_arCam;
        public GameObject HidingBasket;
        // WorldTrackingContents
        public Transform m_WorldTrackingContents;

        [Header("Image Realignment Setting")]
        // settings for initialization
        public AlIGNMODE m_alignMode;
        public IMAGEALIGNMODE m_imageAlignMode;

        public float m_colinearAngleThreshold = 0.1f;
        public float m_colinearPlaneDistanceThreshol = 3f;
        #endregion

        [Header("UnityEvents")]
        public UnityEvent OnAlignModeChanged;
        public UnityEvent OnImageAlignModeChanged;


        private void Awake()
        {
            if (_instance != null && _instance != this) Destroy(this);
            else NcafMainController._instance = this;

            m_ARPlaneManager = GetComponent<ARPlaneManager>();
            m_ARImageManager = GetComponent<ARTrackedImageManager>();
            m_ReferencePointManager = GetComponent<ARReferencePointManager>();
            if (m_ARPlaneManager == null) Debug.LogError("ERR>> AR plane manager not found");
            if (m_ARImageManager == null) Debug.LogError("ERR>> AR image manager not found");
            if (m_ReferencePointManager == null) Debug.LogError("ERR>> AR Ref point manager not found");
        }
        // Start is called before the first frame update
        void Start()
        {

            if (OnAlignModeChanged == null) OnAlignModeChanged = new UnityEvent();
            if (OnImageAlignModeChanged == null) OnImageAlignModeChanged = new UnityEvent();

            if (m_arCam == null)
            {
                m_arCam = Camera.main;
                Debug.LogWarning("WARNING>> AR Camera is not properly set. Main scene camera is used for now");
            }
            m_refTracker = new ReferenceTracker(m_ReferencePointManager);
        }

        // Update is called once per frame
        private void Update()
        {
            if (m_isSanityChecked == false)
            {
                m_isSanityChecked = true;
                SanityCheckARImageInfos();
                ChangeAlignMode(m_alignMode);
                ChangeImageAlignMode(m_imageAlignMode);
            }

            // if the session is not tracking, dim the screen after the time-out period
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                Screen.sleepTimeout = m_timeoutAfterTrackingIsLost;
                return;
            }

            // if the session is tracking, do not dim the screen
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            ////////////////////////////////////////////////////////////////////////////////////////
            foreach (KeyValuePair<string, NcafARImageInfo> info in m_ARImageInfos)
            {
                if (info.Value.m_isFullyTracking) print("Looking at " + info.Key);
            }

            //foreach (ARPlane arPlane in GetARPlaneCurrentlyTracking() )
            //{
            //    print(arPlane.name + ":" + arPlane.trackingState);
            //}
            //foreach(ARTrackedImage arImage in m_imageManager.trackables)
            //{
            //    print(arImage.name +  " - " + arImage.trackingState);
            //}
            //ProcessDetectedImages();
            //Debug.Log("LOG>> # of total images registered: " + m_augementedImageDict.Count);
        }
        private void OnEnable()
        {
            if (m_ARImageManager != null)
                m_ARImageManager.trackedImagesChanged += NcafImageModule.OnTrackedImagesChanged;
            if (m_ARPlaneManager != null)
                m_ARPlaneManager.planesChanged += NcafPlaneModule.OnPlaneChanged;
        }

        private void OnDisable()
        {
            if (m_ARImageManager != null)
                m_ARImageManager.trackedImagesChanged -= NcafImageModule.OnTrackedImagesChanged;
            if (m_ARPlaneManager != null)
                m_ARPlaneManager.planesChanged -= NcafPlaneModule.OnPlaneChanged;
        }

        // foreach practice
        IEnumerable GetARPlaneCurrentlyTracking()
        {
            foreach (ARPlane plane in m_ARPlaneManager.trackables)
            {
                if (plane.trackingState == TrackingState.Tracking) yield return plane;
            }
        }

        List<ARPlane> GetCurrentlyTrackedARPlaneList()
        {
            List<ARPlane> res = new List<ARPlane>();
            foreach (ARPlane plane in m_ARPlaneManager.trackables)
            {
                if (plane.trackingState == TrackingState.Tracking) res.Add(plane);
            }
            if (res.Count == 0) return null;
            return res;
        }

        List<ARTrackedImage> GetCurrentlyTrackedARImageList()
        {
            List<ARTrackedImage> res = new List<ARTrackedImage>();
            foreach (ARTrackedImage img in m_ARImageManager.trackables)
            {
                if (img.trackingState == TrackingState.Tracking) res.Add(img);
            }
            if (res.Count == 0) return null;
            return res;
        }

        public void AddAugmentedImageInfo(NcafARImageInfo augmentedImageInfo)
        {
            Debug.Log("LOG>> Adding " + augmentedImageInfo.m_augmentedImageName);
            try
            {
                m_ARImageInfoDict.Add(augmentedImageInfo.m_augmentedImageName, augmentedImageInfo);
            }
            catch (System.ArgumentException)
            {
                Debug.LogError("ERR>> The AR image \"" + augmentedImageInfo.m_augmentedImageName + "\" has multiple AR Image infos. You should have only one");
            }

        }

        public void SanityCheckARImageInfos()
        {
            ARTrackedImageManager imageManager = GetComponent<ARTrackedImageManager>();
            if (imageManager == null)
            {
                m_alignMode = AlIGNMODE.MANUAL;
                Debug.LogError("ERR>> there is no image manager. ");
                m_isImageManagerAvailable = false;
                return;
            }
            else m_isImageManagerAvailable = true;

            // check if all the AR images has AR image info components associated
            RuntimeReferenceImageLibrary imageLib = (RuntimeReferenceImageLibrary)imageManager.referenceLibrary;

            if (imageLib == null) print("ERR>> cannot cast image lib");

            for (int i = 0; i < imageLib.count; i++)
            {
                XRReferenceImage refImage = imageLib[i];
                if (m_ARImageInfoDict.TryGetValue(refImage.name, out NcafARImageInfo imageInfo) == false)
                {
                    Debug.LogError("ERR>> The AR Image \"" + refImage.name + "\" does not have associated ARImage Info");
                }
            }

        }

        //end of UpdateActivelyTrackedImage
        public Dictionary<TrackableId, ARPlane> GetActivelyTrackingPlanes(Dictionary<TrackableId, ARPlane> arPlaneList)
        {

            //returning dict
            Dictionary<TrackableId, ARPlane> res = new Dictionary<TrackableId, ARPlane>(arPlaneList);

            List<TrackableId> deletionList = new List<TrackableId>();
            //remove
            foreach (KeyValuePair<TrackableId, ARPlane> item in arPlaneList)
            {
                if (item.Value.trackingState != TrackingState.Tracking)
                {
                    deletionList.Add(item.Key);
                }
            }

            //remove
            foreach (TrackableId id in deletionList)
            {
                res.Remove(id);
            }

            return res;
        }

        public void ProcessDetectedImages()
        {

            // if the alignment mode is set to manual, return
            if (m_alignMode == AlIGNMODE.MANUAL) return;

            // If no closest image, return
            ARTrackedImage closestTrackedImage = GetClosestARTrackedImageFromCamera(m_arCam, GetCurrentlyTrackedARImageList());
            if (closestTrackedImage == null) return;
            //Debug.Log("LOG>> Closest AR Image: " + closestTrackedImage.referenceImage.name + " found");

            // Get the closest co-planar AR Plnae
            List<ARPlane> trPlanes = GetCurrentlyTrackedARPlaneList();
            ARPlane coPlane = FindCoplanarPlaneFromImagePose(closestTrackedImage, trPlanes);

            // Alignment begins
            switch (m_alignMode)
            {
                case AlIGNMODE.IMAGE_ONLY:

                    if (m_imageAlignMode == IMAGEALIGNMODE.SINGLE)
                    {
                        AlignModelWithSinglePos(m_WorldTrackingContents, closestTrackedImage, coPlane);
                    }

                    else if (m_imageAlignMode == IMAGEALIGNMODE.INTEPOLATION)
                    {
                        if (m_worldTrackingAlignProcess == null)
                        {
                            m_worldTrackingAlignProcess = AlignModelWithAveragedPos(m_WorldTrackingContents.transform, closestTrackedImage, coPlane);
                            StartCoroutine(m_worldTrackingAlignProcess);
                        }
                    }
                    break;


                case AlIGNMODE.TOUCH:
                    break;

                default:
                    break;
            }
            /////////////////////////////////////////////local contents//////////////////////////
            foreach (ARTrackedImage img in m_ARImageManager.trackables)
            {
                if (img.trackingState == TrackingState.Tracking)
                {
                    NcafMainController.Instance.m_ARImageInfos[img.referenceImage.name].m_isFullyTracking = true;
                    NcafMainController.Instance.ShowLocalContents(img);
                }

                else
                {
                    NcafMainController.Instance.m_ARImageInfos[img.referenceImage.name].m_isFullyTracking = false;
                    NcafMainController.Instance.HideLocalContents(img);
                }
            }
            
            return;        
        }


        /// <summary>
        /// This function alignes the given model with the given ar image with a single position.
        /// </summary>
        /// <param name="modelTransfrom"></param>
        /// <param name="arImage"></param>
        /// <param name="refPlane"></param>
        void AlignModelWithSinglePos(Transform modelTransfrom, ARTrackedImage arImage, ARPlane refPlane = null)
        {

            ResetParent(modelTransfrom);

            Pose pose = new Pose(arImage.transform.position, arImage.transform.rotation);

            ARReferencePoint newRefPoint;
            if (refPlane == null)
            {
                newRefPoint = m_ReferencePointManager.AddReferencePoint(pose);
            }
            else
            {
                newRefPoint = m_ReferencePointManager.AttachReferencePoint(refPlane, pose);
            }


            // if anchor is not ready, exit
            if (newRefPoint == null)
            {
                Debug.LogWarning("WARNING>> refPoint is not ready yet");
                return;
            }

            // Move a model onto the closes image detected
            MoveModelOnARImage(m_WorldTrackingContents, arImage);

            // Update Tracker
            m_refTracker.UpdateRefPlane(refPlane);
            m_refTracker.UpdateRefImage(arImage);
            m_refTracker.UpdateRefPoint(newRefPoint);
            modelTransfrom.SetParent(newRefPoint.transform);


            Debug.Log("LOG>> Single adjustment finished / ref plane: " + refPlane);
        }

        /// <summary>
        /// This function aligns the given model with the given image with an interpolated position
        /// </summary>
        /// <param name="modelTransfrom"></param>
        /// <param name="arImage"></param>
        /// <param name="refPlane"></param>
        /// <returns></returns>
        IEnumerator AlignModelWithAveragedPos(Transform modelTransfrom, ARTrackedImage arImage, ARPlane refPlane = null)
        {
            Debug.Log("LOG>> Interpolation started");
            float timePassed = -Time.deltaTime;
            List<Pose> poseList = new List<Pose>();
            bool isCanceled = false;

            while (timePassed < m_alignInterpolationTimer)
            {
                if (arImage.trackingState != TrackingState.Tracking)
                {
                    Debug.Log("LOG>> Image has been lost from camera frame. Adjustment canceled");
                    isCanceled = true;
                    break;
                }
                Pose pose = new Pose(arImage.transform.position, arImage.transform.rotation);
                poseList.Add(pose);
                timePassed += Time.deltaTime;
                yield return null;
            }

            // finalize translation
            if (!isCanceled)
            {
                Pose poseAveraged = NcHelper.AveragePose(poseList);
                
                ARReferencePoint newRefPoint;
                if (refPlane == null)
                {
                    newRefPoint = m_ReferencePointManager.AddReferencePoint(poseAveraged);
                }
                else
                {
                    newRefPoint = m_ReferencePointManager.AttachReferencePoint(refPlane, poseAveraged);
                }

                // if anchor is not ready, exit
                if (newRefPoint == null)
                {
                    Debug.LogWarning("WARNING>> refPoint is not ready yet");
                    yield break;
                }

                // Move a model onto the closes image detected
                MoveModelOnARImageAveraged(modelTransfrom, arImage, poseAveraged);

                // Update Tracker
                ResetParent(modelTransfrom);
                m_refTracker.UpdateRefPlane(refPlane);
                m_refTracker.UpdateRefImage(arImage);
                m_refTracker.UpdateRefPoint(newRefPoint);
                modelTransfrom.SetParent(newRefPoint.transform);

                Debug.Log("LOG>> interpolated adjustment finished with " + poseList.Count + " data points / ref plane: " + refPlane);
            }
            m_worldTrackingAlignProcess = null;
        }


        /// <summary>
        /// Get the closest AR Tracked Image from the camera
        /// </summary>
        /// <param name="arCam"></param>
        /// <param name="trackedImageDict"></param>
        /// <returns></returns>
        ARTrackedImage GetClosestARTrackedImageFromCamera(Camera arCam, Dictionary<string, ARTrackedImage> trackedImageDict)
        {
            Dictionary<float, ARTrackedImage> debugDict = new Dictionary<float, ARTrackedImage>();
            m_distToTrackedImage = 999999f;
            ARTrackedImage closestImage = null;
            foreach (KeyValuePair<string, ARTrackedImage> item in trackedImageDict)
            {
                ARTrackedImage arImage = item.Value;
                float currentDist = Vector3.Distance(arCam.transform.position, arImage.transform.position);
                //fordebug
                debugDict.Add(currentDist, arImage);

                if (arImage.trackingState == TrackingState.Tracking && currentDist < m_distToTrackedImage)
                {
                    m_distToTrackedImage = currentDist;
                    closestImage = arImage;
                }
            }
            if (closestImage == null)
            {
                Debug.LogError("ERR>> Error in getting the closest tracked Image although " + trackedImageDict.Count + " images detected");
                print(m_distToTrackedImage);
                foreach (KeyValuePair<float, ARTrackedImage> item in debugDict)
                {
                    print(item.Key + " " + item.Value.name + " " + item.Value.trackingState);
                }
            }

            return closestImage;
        }

        ARTrackedImage GetClosestARTrackedImageFromCamera(Camera arCam, List< ARTrackedImage> trackedImageList)
        {
            Dictionary<float, ARTrackedImage> debugDict = new Dictionary<float, ARTrackedImage>();
            m_distToTrackedImage = 999999f;
            ARTrackedImage closestImage = null;
            foreach (ARTrackedImage item in trackedImageList)
            {
                ARTrackedImage arImage = item;
                float currentDist = Vector3.Distance(arCam.transform.position, arImage.transform.position);
                //fordebug
                debugDict.Add(currentDist, arImage);

                if (arImage.trackingState == TrackingState.Tracking && currentDist < m_distToTrackedImage)
                {
                    m_distToTrackedImage = currentDist;
                    closestImage = arImage;
                }
            }
            if (closestImage == null)
            {
                Debug.LogError("ERR>> Error in getting the closest tracked Image although " + trackedImageList.Count + " images detected");
                print(m_distToTrackedImage);
                foreach (KeyValuePair<float, ARTrackedImage> item in debugDict)
                {
                    print(item.Key + " " + item.Value.name + " " + item.Value.trackingState);
                }
            }

            return closestImage;
        }


        bool ResetParent(Transform obj)
        {
            if (obj.GetComponent<NcGameObjectInfo>() == null)
            {
                Debug.LogError("ERR>> the object " + obj.transform.name + " does not have NcGameObjectInfo, so cannot be reset parent");
                return false;
            }
            obj.SetParent(obj.GetComponent<NcGameObjectInfo>().m_initialParent);
            return true;
        }

        void MoveModelOnARImage(Transform content, ARTrackedImage closestARTrackedImage, bool nomalizeScale = true)
        {
            if (m_ARImageInfoDict.TryGetValue(closestARTrackedImage.referenceImage.name, out NcafARImageInfo arImageInfo) == false)
            {
                Debug.LogError("ERR>> Detected AR Image (" + closestARTrackedImage.referenceImage.name + ") does not have AugmentedImageInfo");
                return;
            }


            //Debug.Log("LOG>> moving contents on " + arImageInfo.m_augmentedImageName);
            if (content.GetComponent<NcGameObjectInfo>() == null)
            {
                Debug.Log("ERR>> content " + content.transform.name + " does not have NcGameObjectInfo");
                return;
            }

            NcTransform originalModelTrans = content.GetComponent<NcGameObjectInfo>().OriginalTransformData;
            NcTransform targetInitialTrans = arImageInfo.m_originalNcTransform;
            NcTransform targetMovedTrans = new NcTransform(closestARTrackedImage.transform);

            if (nomalizeScale)
            {
                NcTransform o = originalModelTrans;
                NcTransform t = targetInitialTrans;
                NcTransform t_moved = targetMovedTrans;
                Vector3 v1 = Vector3.one;

                o.lossyScale = v1;
                o.localScale = v1;
                t.lossyScale = v1;
                t.localScale = v1;
                t_moved.lossyScale = v1;
                t_moved.localScale = v1;

                NcTransform newTrans = NcHelper.GetNewGlobalTransformData(o, t, t_moved);
                content.transform.position = newTrans.position;
                content.transform.rotation = newTrans.rotation;
            }

            else
            {
                NcTransform newTrans = NcHelper.GetNewGlobalTransformData(originalModelTrans, targetInitialTrans, targetMovedTrans);
                content.transform.position = newTrans.position;
                content.transform.rotation = newTrans.rotation;
            }

            //Debug.Log("LOG>> finished moving \" " + content.name + " \" on " + arImageInfo.m_augmentedImageName);

        }

        void MoveModelOnARImageAveraged(Transform content, ARTrackedImage arTrackedImage, Pose poseAverage, bool nomalizeScale = true)
        {
            if (m_ARImageInfoDict.TryGetValue(arTrackedImage.referenceImage.name, out NcafARImageInfo arImageInfo) == false)
            {
                Debug.LogError("ERR>> Detected AR Image (" + arTrackedImage.referenceImage.name + ") does not have AugmentedImageInfo");
                return;
            }


            //Debug.Log("LOG>> moving contents on " + arImageInfo.m_augmentedImageName);
            if (content.GetComponent<NcGameObjectInfo>() == null)
            {
                Debug.Log("ERR>> content " + content.transform.name + " does not have NcGameObjectInfo");
                return;
            }

            NcTransform originalModelTrans = content.GetComponent<NcGameObjectInfo>().OriginalTransformData;
            NcTransform targetInitialTrans = arImageInfo.m_originalNcTransform;

            GameObject tempGo = new GameObject();
            tempGo.transform.Translate(poseAverage.position, 0);
            tempGo.transform.rotation = poseAverage.rotation;
            tempGo.transform.SetParent(GetComponent<ARSessionOrigin>().trackablesParent);

            NcTransform targetMovedTrans = new NcTransform(tempGo.transform);
            Destroy(tempGo);



            if (nomalizeScale)
            {
                NcTransform o = originalModelTrans;
                NcTransform t = targetInitialTrans;
                NcTransform t_moved = targetMovedTrans;
                Vector3 v1 = Vector3.one;

                o.lossyScale = v1;
                o.localScale = v1;
                t.lossyScale = v1;
                t.localScale = v1;
                t_moved.lossyScale = v1;
                t_moved.localScale = v1;

                NcTransform newTrans = NcHelper.GetNewGlobalTransformData(o, t, t_moved);
                content.transform.position = newTrans.position;
                content.transform.rotation = newTrans.rotation;
            }

            else
            {
                NcTransform newTrans = NcHelper.GetNewGlobalTransformData(originalModelTrans, targetInitialTrans, targetMovedTrans);
                content.transform.position = newTrans.position;
                content.transform.rotation = newTrans.rotation;
            }

            Debug.Log("LOG>> finished moving \" " + content.name + " \" on " + arImageInfo.m_augmentedImageName);

        }


        public void ShowLocalContents(ARTrackedImage arImage)
        {
            m_ARImageInfoDict.TryGetValue(arImage.referenceImage.name, out NcafARImageInfo info);

            if (info == null)
            {
                Debug.LogError("ERR>> arImage " + arImage.referenceImage.name + " does not have associated ar image info. Local contents will not be shown");
                return;
            }

            List<Transform> contents = info.m_localContents;

            foreach (Transform item in contents)
            {
                MoveModelOnARImage(item, arImage);
                item.SetParent(GetComponent<ARSessionOrigin>().trackablesParent);
                NcHelper.ShowObject(item);
            }
        }
        public void HideLocalContents(ARTrackedImage arImage)
        {
            m_ARImageInfoDict.TryGetValue(arImage.referenceImage.name, out NcafARImageInfo info);

            if (info == null)
            {
                Debug.LogError("ERR>> arImage " + arImage.referenceImage.name + " does not have associated ar image info. Local contents will not be shown");
                return;
            }

            List<Transform> contents = info.m_localContents;
            foreach (Transform item in contents)
            {
                item.SetParent(item.GetComponent<NcGameObjectInfo>().m_initialParent);
                NcHelper.HideObject(item);
            }
        }

        public void ChangeAlignMode(AlIGNMODE mode)
        {
            m_alignMode = mode;
            Debug.Log("LOG>> Align Mode is set to " + mode);
            OnAlignModeChanged.Invoke();
        }

        public void ChangeImageAlignMode(IMAGEALIGNMODE mode)
        {
            m_imageAlignMode = mode;
            Debug.Log("LOG>> Image Align Mode is set to " + mode);
            OnImageAlignModeChanged.Invoke();
        }

        /// <summary>
        /// Find co-planar planes from the plane dict currently tracked
        /// </summary>
        /// <param name="arImage"></param>
        /// <param name="planeDict"></param>
        /// <returns></returns>
        ARPlane FindCoplanarPlaneFromImagePose(ARTrackedImage arImage, Dictionary<TrackableId, ARPlane> planeDict)
        {
            Dictionary<float, ARPlane> coplanarPlanesDict = new Dictionary<float, ARPlane>();

            Vector3 imageUp = arImage.transform.up;
            Vector3 imagePos = arImage.transform.position;

            // find coplanar planes
            foreach (KeyValuePair<TrackableId, ARPlane> item in planeDict)
            {
                Vector3 planeUp = item.Value.transform.up;
                Vector3 planePos = item.Value.transform.position;

                float dotProdut = Vector3.Dot(planeUp.normalized, imageUp.normalized);

                if (1f - m_colinearAngleThreshold < dotProdut && dotProdut < 1f + m_colinearAngleThreshold)
                {
                    float dist = CalcDistPointToPlane(imagePos, planePos, planeUp);
                    if (!coplanarPlanesDict.ContainsKey(dist)) coplanarPlanesDict.Add(dist, item.Value);
                }
            }

            // get the closest one.
            if (coplanarPlanesDict.Count != 0)
            {
                // list of distances
                List<float> distanceList = coplanarPlanesDict.Keys.ToList();
                distanceList.Sort();

                float minDistanceToImage = distanceList[0]; //need to recorded

                if (minDistanceToImage < m_colinearPlaneDistanceThreshol)
                {
                    return coplanarPlanesDict[distanceList[0]];
                }
            }

            return null;
        }

        ARPlane FindCoplanarPlaneFromImagePose(ARTrackedImage arImage, List<ARPlane> planeList)
        {
            if (planeList == null) return null;

            Dictionary<float, ARPlane> coplanarPlanesDict = new Dictionary<float, ARPlane>();

            Vector3 imageUp = arImage.transform.up;
            Vector3 imagePos = arImage.transform.position;

            // find coplanar planes
            foreach (ARPlane plane in planeList)
            {
                Vector3 planeUp = plane.transform.up;
                Vector3 planePos = plane.transform.position;

                float dotProdut = Vector3.Dot(planeUp.normalized, imageUp.normalized);

                if (1f - m_colinearAngleThreshold < dotProdut && dotProdut < 1f + m_colinearAngleThreshold)
                {
                    float dist = CalcDistPointToPlane(imagePos, planePos, planeUp);
                    if (!coplanarPlanesDict.ContainsKey(dist)) coplanarPlanesDict.Add(dist, plane);
                }
            }

            // get the closest one.
            if (coplanarPlanesDict.Count != 0)
            {
                // list of distances
                List<float> distanceList = coplanarPlanesDict.Keys.ToList();
                distanceList.Sort();

                float minDistanceToImage = distanceList[0]; //need to recorded

                if (minDistanceToImage < m_colinearPlaneDistanceThreshol)
                {
                    return coplanarPlanesDict[distanceList[0]];
                }
            }

            return null;
        }
        float CalcDistPointToPlane(Vector3 evalPoint, Vector3 planeOrgin, Vector3 planeNormal)
        {
            Vector3 v = evalPoint - planeOrgin;
            float dist = Vector3.Dot(v, planeNormal.normalized);
            return Mathf.Abs(dist);
        }


        void ResetWorldTrackingContents()
        {
            m_WorldTrackingContents.SetParent(HidingBasket.transform);
            NcTransform originalTrans = m_WorldTrackingContents.GetComponent<NcGameObjectInfo>().OriginalTransformData;
            m_WorldTrackingContents.transform.position = originalTrans.position;
            m_WorldTrackingContents.transform.rotation = originalTrans.rotation;
            m_WorldTrackingContents.transform.localScale = (Vector3)originalTrans.localScale;
        }

        public void QuitApplication()
        {
            Application.Quit();
        }

        public void ResetApplication()
        {
            if (m_arSession == null)
            {
                Debug.LogError("ERR>> AR Session is not connected to the main controller, so reset cannot be done");
                return;
            }
            ResetWorldTrackingContents();
            m_refTracker.ResetTracker();
            m_arSession.Reset();
            Debug.Log("LOG>> AF Session has been successfully reset");
        }
    }

    static class NcafImageModule
    {
        static Dictionary<string, ARTrackedImage> imageBeingTracked = new Dictionary<string, ARTrackedImage>();

        // this function will be called in *every Frame!!*
        public static void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
        {
            imageBeingTracked.Clear();

            foreach (var trackedImage in eventArgs.added)
            {
                if (trackedImage.trackingState == TrackingState.Tracking)
                    imageBeingTracked.Add(trackedImage.referenceImage.name, trackedImage);
            }

            foreach (var trackedImage in eventArgs.updated)
            {
                if (trackedImage.trackingState == TrackingState.Tracking)
                {
                    imageBeingTracked.Add(trackedImage.referenceImage.name, trackedImage);
                    NcafMainController.Instance.m_ARImageInfos[trackedImage.referenceImage.name].m_isFullyTracking = true;
                    NcafMainController.Instance.ShowLocalContents(trackedImage);
                }

                else
                {
                    NcafMainController.Instance.m_ARImageInfos[trackedImage.referenceImage.name].m_isFullyTracking = false;
                    NcafMainController.Instance.HideLocalContents(trackedImage);
                }

            }


            foreach (var trackedImage in eventArgs.removed)
            {
                imageBeingTracked.Remove(trackedImage.referenceImage.name);
            }

            // if no ncaf main controllers, skip
            if (NcafMainController.Instance == null)
            {
                Debug.LogError("ERR>> Main Controller is not assigned (Singleton error)");
                return;
            }

            //if no updated images, skip updating
            if (imageBeingTracked.Count == 0)
            {
                //Debug.Log("LOG>> Tracked image = 0");
                return;
            }

            NcafMainController.Instance.ProcessDetectedImages();
            //Debug.Log(imageBeingTracked.Count + "  has been detected");
        }
    }
    static class NcafPlaneModule
    {
        static Dictionary<TrackableId, ARPlane> m_allDetectedARPlanes = new Dictionary<TrackableId, ARPlane>();
        static Dictionary<TrackableId, ARPlane> m_curretlyDetectedARPlanes = new Dictionary<TrackableId, ARPlane>();
        public static void OnPlaneChanged(ARPlanesChangedEventArgs eventArgs)
        {
            foreach (var arPlane in eventArgs.added)
            {
                //if (m_allDetectedARPlanes.TryGetValue(arPlane.trackableId, out ARPlane plane))
                //{
                //    m_allDetectedARPlanes.Add(arPlane.trackableId, arPlane);
                //}
            }

            foreach (var arPlane in eventArgs.updated)
            {
                //if (m_allDetectedARPlanes.TryGetValue(arPlane.trackableId, out ARPlane plane) == false)
                //{
                //    m_allDetectedARPlanes.Add(arPlane.trackableId, arPlane);
                //}
            }

            foreach (var arPlane in eventArgs.removed)
            {
                //m_allDetectedARPlanes.Remove(arPlane.trackableId);
            }

            //check if planes are subsumed
            //FilterSubsumedPlanes(m_allDetectedARPlanes);

            //m_curretlyDetectedARPlanes = NcafMainController.Instance.GetActivelyTrackingPlanes(m_allDetectedARPlanes);
        }


        public static void FilterSubsumedPlanes(Dictionary<TrackableId, ARPlane> planeDict)
        {
            //planes IDs to remove from the list
            List<TrackableId> deletionList = new List<TrackableId>();
            //dict to add to the list 
            Dictionary<TrackableId, ARPlane> additionDict = new Dictionary<TrackableId, ARPlane>();

            foreach (KeyValuePair<TrackableId, ARPlane> item in planeDict)
            {
                ARPlane planeRef = item.Value;
                while (planeRef.subsumedBy != null)
                {
                    if (planeDict.ContainsKey(item.Key))
                    {
                        deletionList.Add(item.Key);
                    }
                    planeRef = planeRef.subsumedBy;
                }

                if (!additionDict.ContainsKey(planeRef.trackableId))
                    additionDict[planeRef.trackableId] = planeRef;
            }

            //remove subsumed plane
            foreach (TrackableId id in deletionList)
            {
                planeDict.Remove(id);
            }

            //add subsuming plane
            foreach (KeyValuePair<TrackableId, ARPlane> item in additionDict)
            {
                if (!planeDict.ContainsKey(item.Key))
                {
                    planeDict.Add(item.Key, item.Value);
                }
            }

            if (Debug.isDebugBuild)
            {
                //Debug.Log("current all detected planes count: " + m_allDetectedARPlanes.Count);
            }
        }
    }

    public class ReferenceTracker
    {
        private readonly ARReferencePointManager m_refPointManager;
        ARPlane m_refPlane;
        ARReferencePoint m_refPoint;
        ARTrackedImage m_refImage;

        public ReferenceTracker(ARReferencePointManager refPointManager)
        {
            m_refPointManager = refPointManager;
        }

        public void UpdateRefImage(ARTrackedImage arImage)
        {
            m_refImage = arImage;
        }

        public void UpdateRefPlane(ARPlane arPlane)
        {
            m_refPlane = arPlane;
        }

        public void UpdateRefPoint(ARReferencePoint newRefPoint)
        {
            if (m_refPoint != null)
                m_refPointManager.RemoveReferencePoint(m_refPoint);
            m_refPoint = newRefPoint;
        }

        public void ResetTracker()
        {
            m_refPlane = null;
            if (m_refPoint != null)
            {
                m_refPointManager.RemoveReferencePoint(m_refPoint);
                m_refPoint = null;
            }
            m_refImage = null;
        }
    }
}


public class MyComponent
{
    [SerializeField] ARSession m_Session;

    IEnumerator Start()
    {
        if ((ARSession.state == ARSessionState.None ||
            ARSession.state == ARSessionState.CheckingAvailability))
        {
            yield return ARSession.CheckAvailability();
        }

        if (ARSession.state == ARSessionState.Unsupported)
        {
            // Start some fallback experience for unsupported devices
        }
        else
        {
            // Start the AR session
            m_Session.enabled = true;
        }
    }
}