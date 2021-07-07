using System;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgentsExamples;
using Unity.MLAgents.Sensors;
using BodyPart = Unity.MLAgentsExamples.BodyPart;
using Random = UnityEngine.Random;

public class WalkerAgent : Agent
{
    [Header("Virtual Root")]
    //Transform that defines our forward orientation
    //If the hips are already pointing forward you can use the hips
    //otherwise this will act as an override for model space
    public Transform VirtualRoot;

    [Header("Arm Strength Multiplier")]
    [Range(0, 2)]
    public float armStrengthMultiplier = .2f;

    [Header("Walk Speed")]
    [Range(0.1f, 10)]
    [SerializeField]
    //The walking speed to try and achieve
    private float m_TargetWalkingSpeed = 10;

    public float MTargetWalkingSpeed // property
    {
        get { return m_TargetWalkingSpeed; }
        set { m_TargetWalkingSpeed = Mathf.Clamp(value, .1f, m_maxWalkingSpeed); }
    }

    const float m_maxWalkingSpeed = 10; //The max walking speed

    //Should the agent sample a new goal velocity each episode?
    //If true, walkSpeed will be randomly set between zero and m_maxWalkingSpeed in OnEpisodeBegin()
    //If false, the goal velocity will be walkingSpeed
    public bool randomizeWalkSpeedEachEpisode;

    //The direction an agent will walk during training.
    private Vector3 m_WorldDirToWalk = Vector3.right;

    [Header("Target To Walk Towards")] public Transform target; //Target the agent will walk towards during training.

    [Header("Body Parts")] public Transform hips;
    public Transform chest;
    public Transform spine;
    public Transform head;
    public Transform thighL;
    public Transform shinL;
    public Transform footL;
    public Transform thighR;
    public Transform shinR;
    public Transform footR;
    public Transform armL;
    public Transform forearmL;
    public Transform handL;
    public Transform armR;
    public Transform forearmR;
    public Transform handR;

    [Header("ACTUATED RAY SENSOR")]
    //Actuated RayS
    public bool UseVectorObs = true;
    public bool UseActuatedRaycastSensor = false;
    // public List<RayPerceptionSensorComponent3D> RaySensorsList = new List<RayPerceptionSensorComponent3D>();
    public RayPerceptionSensorComponent3D rays1; //Target the agent will walk towards during training.
    public RayPerceptionSensorComponent3D rays2; //Target the agent will walk towards during training.
    public RayPerceptionSensorComponent3D rays3; //Target the agent will walk towards during training.
    public RayPerceptionSensorComponent3D rays4; //Target the agent will walk towards during training.

    public Vector2 MinMaxRayAngles = new Vector2(25, 120);
    public Vector2 MinMaxSpherecastRadius = new Vector2(.25f, 1);
    public float CurrentRayAngleLerp = .5f;
    public float CurrentSpherecastRadiusLerp = .5f;

    [Header("Ray Sensors")]

    //This will be used as a stabilized model space reference point for observations
    //Because ragdolls can move erratically during training, using a stabilized reference transform improves learning
    // OrientationCubeController m_OrientationCube;

    //The indicator graphic gameobject that points towards the target
    DirectionIndicator m_DirectionIndicator;
    JointDriveController m_JdController;
    EnvironmentParameters m_ResetParams;
    public Vector3 bodyVelocityLastFrame;

    public override void Initialize()
    {
        // m_OrientationCube = GetComponentInChildren<OrientationCubeController>();
        m_DirectionIndicator = GetComponentInChildren<DirectionIndicator>();

        //Setup each body part
        m_JdController = GetComponent<JointDriveController>();
        m_JdController.SetupBodyPart(hips);
        m_JdController.SetupBodyPart(chest);
        m_JdController.SetupBodyPart(spine);
        m_JdController.SetupBodyPart(head);
        m_JdController.SetupBodyPart(thighL);
        m_JdController.SetupBodyPart(shinL);
        m_JdController.SetupBodyPart(footL);
        m_JdController.SetupBodyPart(thighR);
        m_JdController.SetupBodyPart(shinR);
        m_JdController.SetupBodyPart(footR);
        m_JdController.SetupBodyPart(armL);
        m_JdController.SetupBodyPart(forearmL);
        m_JdController.SetupBodyPart(handL);
        m_JdController.SetupBodyPart(armR);
        m_JdController.SetupBodyPart(forearmR);
        m_JdController.SetupBodyPart(handR);

        m_ResetParams = Academy.Instance.EnvironmentParameters;

        SetResetParameters();
    }

    /// <summary>
    /// Loop over body parts and reset them to initial conditions.
    /// </summary>
    public override void OnEpisodeBegin()
    {
        //Reset all of the body parts
        foreach (var bodyPart in m_JdController.bodyPartsDict.Values)
        {
            bodyPart.Reset(bodyPart);
        }

        //Random start rotation to help generalize
        hips.rotation = Quaternion.Euler(0, Random.Range(0.0f, 360.0f), 0);

        // UpdateOrientationObjects();

        //Set our goal walking speed
        MTargetWalkingSpeed =
            randomizeWalkSpeedEachEpisode ? Random.Range(0.1f, m_maxWalkingSpeed) : MTargetWalkingSpeed;

        SetResetParameters();
    }

    /// <summary>
    /// Add relevant information on each body part to observations.
    /// </summary>
    public void CollectObservationBodyPart(BodyPart bp, VectorSensor sensor)
    {
        //GROUND CHECK
        sensor.AddObservation(bp.groundContact.touchingGround); // Is this bp touching the ground

        //Get velocities in the context of our orientation cube's space
        //Note: You can get these velocities in world space as well but it may not train as well.
        // sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.velocity));
        // sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.angularVelocity));
        sensor.AddObservation(VirtualRoot.InverseTransformDirection(bp.rb.angularVelocity));

        //Get position relative to hips in the context of our orientation cube's space
        // sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.position - hips.position));

        if (bp.rb.transform != hips && bp.rb.transform != handL && bp.rb.transform != handR)
        {
            sensor.AddObservation(bp.rb.transform.localRotation);
            sensor.AddObservation(bp.currentStrength / m_JdController.maxJointForceLimit);
        }
    }

    /// <summary>
    /// Loop over body parts to add them to observation.
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // print("colobsv");

        // var cubeForward = m_OrientationCube.transform.forward;

        // //velocity we want to match
        // var velGoal = cubeForward * MTargetWalkingSpeed;
        //ragdoll's avg vel
        // var avgVel = GetAvgVelocity();

        var currentBodyVel = m_JdController.bodyPartsDict[hips].rb.velocity;
        var bodyAccel = (currentBodyVel - bodyVelocityLastFrame) / Time.fixedDeltaTime;
        sensor.AddObservation(VirtualRoot.InverseTransformDirection(bodyAccel));
        bodyVelocityLastFrame = currentBodyVel;


        var targetDir = target.position - VirtualRoot.position;

        //velocity we want to match
        var velGoal = targetDir * MTargetWalkingSpeed;
        // var velGoal = cubeForward * MTargetWalkingSpeed;
        //ragdoll's avg vel
        var avgVel = GetAvgVelocity();

        //current ragdoll velocity. normalized
        sensor.AddObservation(Vector3.Distance(velGoal, avgVel));
        //avg body vel relative to cube
        sensor.AddObservation(VirtualRoot.InverseTransformDirection(avgVel));
        //vel goal relative to cube
        sensor.AddObservation(VirtualRoot.InverseTransformDirection(velGoal));

        // var bodyForward = -body.up;
        // //rotation deltas
        // sensor.AddObservation(Quaternion.FromToRotation(bodyForward, cubeForward));
        // sensor.AddObservation(Quaternion.FromToRotation(bodyForward, cubeForward));

        //Position of target position relative to cube
        sensor.AddObservation(VirtualRoot.InverseTransformPoint(target.transform.position));

        foreach (var bodyPart in m_JdController.bodyPartsList)
        {
            CollectObservationBodyPart(bodyPart, sensor);
        }

        if (UseActuatedRaycastSensor)
        {
            sensor.AddObservation(CurrentRayAngleLerp);
            sensor.AddObservation(CurrentSpherecastRadiusLerp);
        }
        // //avg body vel relative to cube
        // sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(avgVel));
        // //vel goal relative to cube
        // sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(velGoal));

        // //rotation deltas
        // sensor.AddObservation(Quaternion.FromToRotation(hips.forward, cubeForward));
        // sensor.AddObservation(Quaternion.FromToRotation(head.forward, cubeForward));

        // //Position of target position relative to cube
        // sensor.AddObservation(m_OrientationCube.transform.InverseTransformPoint(target.transform.position));

        // foreach (var bodyPart in m_JdController.bodyPartsList)
        // {
        //     CollectObservationBodyPart(bodyPart, sensor);
        // }
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)

    {
        var bpDict = m_JdController.bodyPartsDict;
        var i = -1;

        var continuousActions = actionBuffers.ContinuousActions;
        bpDict[chest].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);
        bpDict[spine].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);

        bpDict[thighL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[thighR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[shinL].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[shinR].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[footR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);
        bpDict[footL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);

        bpDict[armL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[armR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[forearmL].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[forearmR].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[head].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);

        //update joint strength settings
        bpDict[chest].SetJointStrength(continuousActions[++i]);
        bpDict[spine].SetJointStrength(continuousActions[++i]);
        bpDict[head].SetJointStrength(continuousActions[++i]);
        bpDict[thighL].SetJointStrength(continuousActions[++i]);
        bpDict[shinL].SetJointStrength(continuousActions[++i]);
        bpDict[footL].SetJointStrength(continuousActions[++i]);
        bpDict[thighR].SetJointStrength(continuousActions[++i]);
        bpDict[shinR].SetJointStrength(continuousActions[++i]);
        bpDict[footR].SetJointStrength(continuousActions[++i]);
        bpDict[armL].SetJointStrength(continuousActions[++i] - armStrengthMultiplier);
        bpDict[forearmL].SetJointStrength(continuousActions[++i] - armStrengthMultiplier);
        bpDict[armR].SetJointStrength(continuousActions[++i] - armStrengthMultiplier);
        bpDict[forearmR].SetJointStrength(continuousActions[++i] - armStrengthMultiplier);
        // bpDict[armL].SetJointStrength(continuousActions[++i]);
        // bpDict[forearmL].SetJointStrength(continuousActions[++i]);
        // bpDict[armR].SetJointStrength(continuousActions[++i]);
        // bpDict[forearmR].SetJointStrength(continuousActions[++i]);

        // ACTUATED SENSOR STUFF
        if (UseActuatedRaycastSensor)
        {
            CurrentRayAngleLerp = (continuousActions[++i] + 1)/2;
            CurrentSpherecastRadiusLerp = (continuousActions[++i] + 1)/2;
            UpdateRayAngles(rays1);
            UpdateRayAngles(rays2);
            UpdateRayAngles(rays3);
            UpdateRayAngles(rays4);
        }
        GiveRewards();
    }

    // private void Update()
    // {
    //     print("update");
    // }
    private int fuStep = 0;
    private void FixedUpdate()
    {
        if (fuStep % (127 * 5) == 0)
        {
            // UpdateTimer();
            // fuStep =
            var timeSinceLast = Time.realtimeSinceStartup - lastTime;
            print(timeSinceLast/(float)(127 * 5));
            lastTime = Time.realtimeSinceStartup;
        }

        fuStep += 1;
        // timer +=
        // print("fu");
    }

    // private float timer = 0;
    private float lastTime = 0;
    // private int decisionStep = 0;

    // void UpdateTimer()
    // {
    //     var timeDeltaSinceLast = Time.
    //     if(Time.realtimeSinceStartup)
    //
    // }

    void UpdateRayAngles(RayPerceptionSensorComponent3D raySensor)
    {
        raySensor.MaxRayDegrees = Mathf.Lerp(MinMaxRayAngles.x, MinMaxRayAngles.y, CurrentRayAngleLerp);
        raySensor.SphereCastRadius = Mathf.Lerp(MinMaxSpherecastRadius.x, MinMaxSpherecastRadius.y, CurrentSpherecastRadiusLerp);
    }

    void GiveRewards()
    {
        var targetDir = target.position - VirtualRoot.position;

        // Set reward for this step according to mixture of the following elements.
        // a. Match target speed
        //This reward will approach 1 if it matches perfectly and approach zero as it deviates
        var matchSpeedReward = GetMatchingVelocityReward(targetDir.normalized * MTargetWalkingSpeed, GetAvgVelocity());

        // b. Rotation alignment with target direction.
        //This reward will approach 1 if it faces the target direction perfectly and approach zero as it deviates
        var lookAtTargetReward = (Vector3.Dot(targetDir.normalized, hips.forward) + 1) * .5F;

        AddReward(matchSpeedReward * lookAtTargetReward);
    }

    //Returns the average velocity of all of the body parts
    //Using the velocity of the hips only has shown to result in more erratic movement from the limbs, so...
    //...using the average helps prevent this erratic movement
    Vector3 GetAvgVelocity()
    {
        Vector3 velSum = Vector3.zero;

        //ALL RBS
        int numOfRb = 0;
        foreach (var item in m_JdController.bodyPartsList)
        {
            numOfRb++;
            velSum += item.rb.velocity;
        }

        var avgVel = velSum / numOfRb;
        return avgVel;
    }

    //normalized value of the difference in avg speed vs goal walking speed.
    public float GetMatchingVelocityReward(Vector3 velocityGoal, Vector3 actualVelocity)
    {
        //distance between our actual velocity and goal velocity
        var velDeltaMagnitude = Mathf.Clamp(Vector3.Distance(actualVelocity, velocityGoal), 0, MTargetWalkingSpeed);

        //return the value on a declining sigmoid shaped curve that decays from 1 to 0
        //This reward will approach 1 if it matches perfectly and approach zero as it deviates
        return Mathf.Pow(1 - Mathf.Pow(velDeltaMagnitude / MTargetWalkingSpeed, 2), 2);
    }

    /// <summary>
    /// Agent touched the target
    /// </summary>
    public void TouchedTarget()
    {
        AddReward(1f);
    }

    public void SetTorsoMass()
    {
        m_JdController.bodyPartsDict[chest].rb.mass = m_ResetParams.GetWithDefault("chest_mass", 8);
        m_JdController.bodyPartsDict[spine].rb.mass = m_ResetParams.GetWithDefault("spine_mass", 8);
        m_JdController.bodyPartsDict[hips].rb.mass = m_ResetParams.GetWithDefault("hip_mass", 8);
    }

    public void SetResetParameters()
    {
        // SetTorsoMass();
    }
}
