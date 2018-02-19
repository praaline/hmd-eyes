﻿using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using Pupil;

public class PupilTools : MonoBehaviour
{
	private static PupilSettings Settings
	{
		get { return PupilSettings.Instance; }
	}

	private static EStatus _dataProcessState = EStatus.Idle;
	public static EStatus DataProcessState
	{
		get { return _dataProcessState; }
		set
		{
			_dataProcessState = value;
			if (Calibration.Marker != null)
				Calibration.Marker.SetActive (_dataProcessState == EStatus.Calibration);
		}
	}
	static EStatus stateBeforeCalibration = EStatus.Idle;

	//InspectorGUI repaint
	public delegate void GUIRepaintAction ();
	public delegate void OnCalibrationStartDeleg ();
	public delegate void OnCalibrationEndDeleg ();
	public delegate void OnCalibrationFailedDeleg ();
	public delegate void OnConnectedDelegate ();
	public delegate void OnDisconnectingDelegate ();
	public delegate void OnReceiveDataDelegate (string topic, Dictionary<string,object> dictionary, byte[] thirdFrame = null);

	public static event GUIRepaintAction WantRepaint;
	public static event OnCalibrationStartDeleg OnCalibrationStarted;
	public static event OnCalibrationEndDeleg OnCalibrationEnded;
	public static event OnCalibrationFailedDeleg OnCalibrationFailed;
	public static event OnConnectedDelegate OnConnected;
	public static event OnDisconnectingDelegate OnDisconnecting;
	public static event OnReceiveDataDelegate OnReceiveData;

	#region Recording

	private static bool isRecording = false;
	public static void StartRecording ()
	{
		var _p = Settings.recorder.GetRecordingPath ().Substring (2);

		Send (new Dictionary<string,object> {
			{ "subject","recording.should_start" },
			 {
				"session_name",
				_p
			}
		});

		isRecording = true;

		recordingString = "Timestamp,Identifier,PupilPositionX,PupilPositionY,PupilPositionZ,UnityWorldPositionX,UnityWorldPositionY,UnityWorldPositionZ\n";
	}

	private static string recordingString;

	public static void StopRecording ()
	{
		Send (new Dictionary<string,object> { { "subject","recording.should_stop" } });

		isRecording = false;
	}

	private static Vector3 unityWorldPosition;
	private static void AddToRecording( string identifier, Vector3 position, bool isViewportPosition = false )
	{
		var timestamp = TimestampForDictionary (gazeDictionary);

		if (isViewportPosition)
			unityWorldPosition = Settings.currentCamera.ViewportToWorldPoint (position + Vector3.forward);
		else
			unityWorldPosition = Settings.currentCamera.cameraToWorldMatrix.MultiplyPoint3x4 (position);

		if (!isViewportPosition)
			position.y *= -1;				// Pupil y axis is inverted

		recordingString += string.Format ( "{0},{1},{2},{3},{4},{5},{6},{7}\n"
			,timestamp.ToString ("F4")
			,identifier
			,position.x.ToString ("F4"),position.y.ToString ("F4"),position.z.ToString ("F4")
			,unityWorldPosition.x.ToString ("F4"),unityWorldPosition.y.ToString ("F4"),unityWorldPosition.z.ToString ("F4")
		);
	}

	public static void SaveRecording(string toPath)
	{
		string filePath = toPath + "/" + "UnityGazeExport.csv";
		File.WriteAllText(filePath, recordingString);
	}

	#endregion

	public static Dictionary<string, object> pupil0Dictionary;
	public static Dictionary<string, object> pupil1Dictionary;
	private static Dictionary<string, object> _gazeDictionary;
	public static Dictionary<string, object> gazeDictionary
	{
		get
		{
			return _gazeDictionary;
		}
		set
		{
			_gazeDictionary = value;
			UpdateGaze ();
			UpdateEyeID ();
		}
	}

	private static string[] gazeKeys = { "gaze_point_3d", "norm_pos", "eye_centers_3d" , "gaze_normals_3d" };
	private static string eyeDataKey;
	private static void UpdateGaze()
	{
		foreach (var key in gazeKeys)
		{
			if (gazeDictionary.ContainsKey (key))
			{
				switch (key)
				{
				case "norm_pos": // 2D case
					eyeDataKey = key + "_" + stringForEyeID (); // we add the identifier to the key
					var position2D = Position (gazeDictionary [key], false);
					PupilData.AddGazeToEyeData (eyeDataKey, position2D);
					if (isRecording)
						AddToRecording (eyeDataKey, position2D, true);
					break;
				case "eye_centers_3d":
				case "gaze_normals_3d":
					// in case of eye_centers_3d and gaze_normals_3d, we get an dictionary with one positional object for each eye id (the key)
					if (gazeDictionary [key] is Dictionary<object,object>)
						foreach (var item in (gazeDictionary[key] as Dictionary<object,object>))
						{
							eyeDataKey = key + "_" + item.Key.ToString ();
							var position = Position (item.Value, true);
							position.y *= -1f;							// Pupil y axis is inverted
							PupilData.AddGazeToEyeData (eyeDataKey,position);
						}
					break;
				default:
					var position3D = Position (gazeDictionary [key], true);
					position3D.y *= -1f;								// Pupil y axis is inverted
					PupilData.AddGazeToEyeData (key, position3D);
					if (isRecording)
						AddToRecording (key, position3D);
					break;
				}
			}
		}
	}

	private static object IDo;
	private static void UpdateEyeID ()
	{
		string id = "";

		if (gazeDictionary != null)
			id = EyeIDForDictionary (gazeDictionary);
			
		PupilData.UpdateCurrentEyeID(id);
	}

	public static string stringForEyeID ()
	{
		object IDo;
		if (gazeDictionary == null)
			return null;

		bool isID = gazeDictionary.TryGetValue ("id", out IDo);

		if (isID)
		{
			return IDo.ToString ();

		}
		else
		{
			return null;
		}
	}

	private static object[] position_o;
	private static Vector3 Position (object position, bool applyScaling)
	{
		position_o = position as object[];
		Vector3 result = Vector3.zero;
		if (position_o.Length != 2 && position_o.Length != 3)
			UnityEngine.Debug.Log ("Array length not supported");
		else
		{
			result.x = (float)(double)position_o [0];
			result.y = (float)(double)position_o [1];
			if ( position_o.Length == 3)
				result.z = (float)(double)position_o [2];
		}
		if (applyScaling)
			result /= PupilSettings.PupilUnitScalingFactor;
		return result;
	}

	public static float TimestampForDictionary(Dictionary<string,object> dictionary)
	{
		object timestamp;
		dictionary.TryGetValue ("timestamp", out timestamp);
		return (float)(double)timestamp;
	}

	public static float ConfidenceForDictionary(Dictionary<string,object> dictionary)
	{
		object conf0;
		dictionary.TryGetValue ("confidence", out conf0);
		return (float)(double)conf0;
	}

	public static float Confidence (int eyeID)
	{
		if (eyeID == PupilData.rightEyeID && pupil0Dictionary != null)
			return ConfidenceForDictionary (pupil0Dictionary);
		else if (eyeID == PupilData.leftEyeID && pupil1Dictionary != null)
			return ConfidenceForDictionary (pupil1Dictionary); 
		else
			return 0;
	}

	public static string EyeIDForDictionary(Dictionary<string,object> dictionary)
	{
		string id = "";
		if (dictionary.TryGetValue ("id", out IDo))
			id = IDo.ToString ();
		return id;
	}

	public static Dictionary<object,object> BaseData ()
	{
		object o;
		gazeDictionary.TryGetValue ("base_data", out o);
		return o as Dictionary<object,object>;
	}
	#region Calibration

	public static void RepaintGUI ()
	{
		if (WantRepaint != null)
			WantRepaint ();
	}

	public static Connection Connection
	{
		get { return Settings.connection; }
	}
	public static bool IsConnected
	{
		get { return Connection.isConnected; }
		set { Connection.isConnected = value; }
	}
	public static IEnumerator Connect(bool retry = false, float retryDelay = 5f)
	{
		yield return new WaitForSeconds (3f);

		while (!IsConnected) 
		{
			Connection.InitializeRequestSocket ();

			if (!IsConnected)
            {
				if (retry) 
				{
                    UnityEngine.Debug.Log("Could not connect, Re-trying in 5 seconds ! ");
					yield return new WaitForSeconds (retryDelay);

				} else 
				{
					Connection.TerminateContext ();
					yield return null;
				}

			} 
			//yield return null;
        }
        UnityEngine.Debug.Log(" Succesfully connected to Pupil Service ! ");

        StartEyeProcesses();
        RepaintGUI();
		if (OnConnected != null)
        	OnConnected();
        yield break;
    }

	public static void SubscribeTo (string topic)
	{
		Connection.InitializeSubscriptionSocket (topic);
	}

	public static void UnSubscribeFrom (string topic)
	{
		Connection.CloseSubscriptionSocket (topic);
	}

	public static bool Send(Dictionary<string,object> dictionary)
	{
		return Connection.sendRequestMessage (dictionary);
	}

	public static Calibration Calibration
	{
		get { return Settings.calibration; }
	}
	private static Calibration.Mode _calibrationMode = Calibration.Mode._2D;
	public static Calibration.Mode CalibrationMode
	{
		get { return _calibrationMode; }
		set 
		{
			if (IsConnected && !Connection.Is3DCalibrationSupported ())
				value = Calibration.Mode._2D;

			if (_calibrationMode != value)
			{
				_calibrationMode = value;

				if (IsConnected)
					SetDetectionMode ();
			}
		}
	}
	public static Calibration.Type CalibrationType
	{
		get { return Calibration.currentCalibrationType; }
	}

	public static void StartCalibration ()
	{
		if (OnCalibrationStarted != null)
			OnCalibrationStarted ();
		else
		{
			print ("No 'calibration started' delegate set");
		}

		Calibration.InitializeCalibration ();

		stateBeforeCalibration = DataProcessState;
		DataProcessState = EStatus.Calibration;
		SubscribeTo ("notify.calibration.successful");
		SubscribeTo ("notify.calibration.failed");
		SubscribeTo ("pupil.");

		Send (new Dictionary<string,object> {
			{ "subject","start_plugin" },
			 {
				"name",
				CalibrationType.pluginName
			}
		});
		Send (new Dictionary<string,object> {
			{ "subject","calibration.should_start" },
			 {
				"hmd_video_frame_size",
				new float[] {
					1000,
					1000
				}
			},
			 {
				"outlier_threshold",
				35
			},
			{
				"translation_eye0",
				Calibration.rightEyeTranslation
			},
			{
				"translation_eye1",
				Calibration.leftEyeTranslation
			}
		});

		_calibrationData.Clear ();

		RepaintGUI ();
	}

	public static void StopCalibration ()
	{
		DataProcessState = stateBeforeCalibration;
		Send (new Dictionary<string,object> { { "subject","calibration.should_stop" } });
	}

	public static void CalibrationFinished ()
	{
		DataProcessState = EStatus.Idle;

		print ("Calibration finished");

		UnSubscribeFrom ("notify.calibration.successful");
		UnSubscribeFrom ("notify.calibration.failed");
		UnSubscribeFrom ("pupil.");

		if (OnCalibrationEnded != null)
			OnCalibrationEnded ();
		else
		{
			print ("No 'calibration ended' delegate set");
		}
	}

	public static void CalibrationFailed ()
	{
		DataProcessState = EStatus.Idle;

		if (OnCalibrationFailed != null)
			OnCalibrationFailed ();
		else
		{
			print ("No 'calibration failed' delegate set");
		}
	}

	private static List<Dictionary<string,object>> _calibrationData = new List<Dictionary<string,object>> ();
	public static void AddCalibrationReferenceData ()
	{
		Send (new Dictionary<string,object> {
			{ "subject","calibration.add_ref_data" },
			{
				"ref_data",
				_calibrationData.ToArray ()
			}
		});

		if (Settings.debug.printSampling)
		{
			print ("Sending ref_data");

			string str = "";

			foreach (var element in _calibrationData)
			{
				foreach (var i in element)
				{
					if (i.Key == "norm_pos")
					{
						str += "|| " + i.Key + " | " + ((System.Single[])i.Value) [0] + " , " + ((System.Single[])i.Value) [1];
					} else
					{
						str += "|| " + i.Key + " | " + i.Value.ToString ();
					}
				}
				str += "\n";

			}

			print (str);
		}

		//Clear the current calibration data, so we can proceed to the next point if there is any.
		_calibrationData.Clear ();
	}

	public static void AddCalibrationPointReferencePosition (float[] position, float timestamp)
	{
		if (CalibrationMode == Calibration.Mode._3D)
			for (int i = 0; i < position.Length; i++)
				position [i] *= PupilSettings.PupilUnitScalingFactor;
		
		_calibrationData.Add ( new Dictionary<string,object> () {
			{ CalibrationType.positionKey, position }, 
			{ "timestamp", timestamp },
			{ "id", PupilData.leftEyeID }
		});
		_calibrationData.Add ( new Dictionary<string,object> () {
			{ CalibrationType.positionKey, position }, 
			{ "timestamp", timestamp },
			{ "id", PupilData.rightEyeID }
		});
	}

	public static void UpdateCalibrationMarkerColor( string eyeID, float value )
	{
		var currentColor = Calibration.Marker.color;
		if (eyeID == "0")
			currentColor.g = value;
		else if (eyeID == "1")
			currentColor.b = value;
		Calibration.Marker.color = currentColor;
	}

	#endregion

	public static bool StartEyeProcesses ()
	{
		var startLeftEye = new Dictionary<string,object> {
			{ "subject","eye_process.should_start." + PupilData.leftEyeID.ToString() },
			{ "eye_id", PupilData.leftEyeID	}, 
			{ "delay", 0.1f }
		};
		var startRightEye = new Dictionary<string,object> {
			{ "subject","eye_process.should_start."  + PupilData.rightEyeID.ToString() }, 
			{ "eye_id", PupilData.rightEyeID },
			{ "delay", 0.2f }
		};

		if ( SetDetectionMode() )
			if ( Send (startLeftEye) )
				if ( Send (startRightEye) )
					return true;

		return false;
	}

	public static void Disconnect()
	{
		if (OnDisconnecting != null)
			OnDisconnecting ();
		
		if (DataProcessState == EStatus.Calibration)
			StopCalibration ();
		
		StopEyeProcesses ();

		Connection.CloseSockets ();
	}

	public static bool ReceiveDataIsSet { get { return OnReceiveData != null; } }
	public static void ReceiveData (string topic, Dictionary<string,object> dictionary, byte[] thirdFrame = null)
	{
		if (OnReceiveData != null)
			OnReceiveData (topic, dictionary, thirdFrame);
		else
			UnityEngine.Debug.Log ("OnReceiveData is not set");
	}

	public static bool StopEyeProcesses ()
	{
		var stopLeftEye = new Dictionary<string,object> {
			{ "subject","eye_process.should_stop." + PupilData.leftEyeID.ToString() },
			{ "eye_id", PupilData.leftEyeID	}, 
			{ "delay", 0.1f }
		};
		var stopRightEye = new Dictionary<string,object> {
			{ "subject","eye_process.should_stop." + PupilData.rightEyeID.ToString() }, 
			{ "eye_id", PupilData.rightEyeID },
			{ "delay", 0.2f }
		};

		if ( Send (stopLeftEye) )
			if ( Send (stopRightEye) )
				return true;

		return false;
	}

	public static void StartBinocularVectorGazeMapper ()
	{
		Send (new Dictionary<string,object> { { "subject","" }, { "name", "Binocular_Vector_Gaze_Mapper" } });
	}

	public static bool SetDetectionMode()
	{
		return Send (new Dictionary<string,object> { { "subject", "set_detection_mapping_mode" }, { "mode", CalibrationType.name } });
	}

	public static void StartFramePublishing ()
	{
		Settings.framePublishing.StreamCameraImages = true;
		Settings.framePublishing.InitializeFramePublishing ();

		Send (new Dictionary<string,object> { { "subject","start_plugin" }, { "name","Frame_Publisher" } });

		SubscribeTo ("frame.");
		//		print ("frame publish start");
		//Send (new Dictionary<string,object> { { "subject","frame_publishing.started" } });
	}

	public static void UpdateFramePublishingImage (int eyeID, byte[] rawData)
	{
		if (eyeID == 0)
			Settings.framePublishing.raw0 = rawData;
		else
			Settings.framePublishing.raw1 = rawData;
	}

	public static void StopFramePublishing ()
	{
		UnSubscribeFrom ("frame.");

		Settings.framePublishing.StreamCameraImages = false;

		Send (new Dictionary<string,object> { { "subject","stop_plugin" }, { "name", "Frame_Publisher" } });
	}
}
