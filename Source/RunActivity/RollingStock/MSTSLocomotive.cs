﻿/* LOCOMOTIVE CLASSES
 * 
 * Used a a base for Steam, Diesel and Electric locomotive classes.
 * 
 * A locomotive is represented by two classes:
 *  LocomotiveSimulator - defines the behaviour, ie physics, motion, power generated etc
 *  LocomotiveViewer - defines the appearance in a 3D viewer including animation for wipers etc
 *  
 * Both these classes derive from corresponding classes for a basic TrainCar
 *  TrainCarSimulator - provides for movement, rolling friction, etc
 *  TrainCarViewer - provides basic animation for running gear, wipers, etc
 *  
 * Locomotives can either be controlled by a player, 
 * or controlled by the train's MU signals for brake and throttle etc.
 * The player controlled loco generates the MU signals which pass along to every
 * unit in the train.
 * For AI trains, the AI software directly generates the MU signals - there is no
 * player controlled train.
 * 
 * The end result of the physics calculations for the the locomotive is
 * a TractiveForce and a FrictionForce ( generated by the TrainCar class )
 * 
 */
/// COPYRIGHT 2009 by the Open Rails project.
/// This code is provided to enable you to contribute improvements to the open rails program.  
/// Use of the code for any other purpose or distribution of the code to anyone else
/// is prohibited without specific written permission from admin@openrails.org.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MSTS;



namespace ORTS
{

    ///////////////////////////////////////////////////
    ///   SIMULATION BEHAVIOUR
    ///////////////////////////////////////////////////


    /// <summary>
    /// Adds Throttle, Direction, Horn, Sander and Wiper control
    /// to the basic TrainCar.
    /// Use as a base for Electric, Diesel or Steam locomotives.
    /// </summary>
    public class MSTSLocomotive: MSTSWagon
    {
        // simulation parameters
        public bool Horn = false;
        public bool Bell = false;
        public bool Sander = false;  
        public bool Wiper = false;
        public bool BailOff = false;
        public float MaxPowerW;
        public float MaxForceN;
        public float MaxSpeedMpS = 1e3f;
        public float MainResPressurePSI = 130;
        public bool CompressorOn = false;
        public float AverageForceN = 0;
        // by GeorgeS
        public bool CabLightOn = false;
        public bool ShowCab = true;

        // wag file data
        public string CabSoundFileName = null;
        public string CVFFileName = null;
        public float MaxMainResPressurePSI = 130;
        public float MainResVolumeFT3 = 10;
        public float CompressorRestartPressurePSI = 110;
        public float MainResChargingRatePSIpS = .4f;
        public float EngineBrakeReleaseRatePSIpS = 12.5f;
        public float EngineBrakeApplyRatePSIpS = 12.5f;
        public float BrakePipeTimeFactorS = .003f;
        public float BrakeServiceTimeFactorS = 1.009f;
        public float BrakeEmergencyTimeFactorS = .1f;
        public float BrakePipeChargingRatePSIpS = Program.BrakePipeChargingRatePSIpS;
        public Interpolator2D TractiveForceCurves = null;
        public Interpolator2D DynamicBrakeForceCurves = null;
        public float DynamicBrakeSpeed1 = 3;
        public float DynamicBrakeSpeed2 = 18;
        public float DynamicBrakeSpeed3 = 23;
        public float DynamicBrakeSpeed4 = 35;
        public float MaxDynamicBrakeForceN = 0;
        public bool DynamicBrakeAutoBailOff = false;
        public float MaxContinuousForceN;
        public float ContinuousForceTimeFactor = 1800;

        public CVFFile CVFFile = null;

        public MSTSNotchController  ThrottleController;
        public MSTSBrakeController  TrainBrakeController;
        public MSTSBrakeController  EngineBrakeController;
        public AirSinglePipe.ValveState EngineBrakeState = AirSinglePipe.ValveState.Lap;
        public MSTSNotchController  DynamicBrakeController;

        public MSTSLocomotive(string wagPath, TrainCar previousCar)
            : base(wagPath, previousCar)
        {
            //Console.WriteLine("loco {0} {1} {2}", MaxPowerW, MaxForceN, MaxSpeedMpS);
        }

        /// <summary>
        /// This initializer is called when we haven't loaded this type of car before
        /// and must read it new from the wag file.
        /// </summary>
        public override void InitializeFromWagFile(string wagFilePath)
        {
            TrainBrakeController = new MSTSBrakeController();
            EngineBrakeController = new MSTSBrakeController();
            DynamicBrakeController = new MSTSNotchController();
            base.InitializeFromWagFile(wagFilePath);

            if (ThrottleController == null)
            {
                //If no controller so far, we create a default one
                ThrottleController = new MSTSNotchController();
                ThrottleController.StepSize = 0.1f;
            }

            if (CVFFileName != null)
            {
                string CVFFilePath = Path.GetDirectoryName(WagFilePath) + @"\CABVIEW\" + CVFFileName;
                CVFFile = new CVFFile(CVFFilePath);

                // Set up camera locations for the cab views
                for( int i = 0; i < CVFFile.Locations.Count; ++i )
                {
                    if (i >= CVFFile.Locations.Count || i >= CVFFile.Directions.Count)
                    {
                        Trace.TraceError("Position or Direction missing in " + CVFFilePath);
                        break;
                    }
                    ViewPoint viewPoint = new ViewPoint();
                    viewPoint.Location = CVFFile.Locations[i];
                    viewPoint.StartDirection = CVFFile.Directions[i];
                    viewPoint.RotationLimit = new Vector3( 0,0,0 );  // cab views have a fixed head position
                    FrontCabViewpoints.Add(viewPoint);
                }
            }

            IsDriveable = true;
            if (!TrainBrakeController.IsValid())
                TrainBrakeController = new MSTSBrakeController(); //create a blank one
            if (!EngineBrakeController.IsValid())
                EngineBrakeController = null;
            if (!DynamicBrakeController.IsValid())
                DynamicBrakeController = null;
            if (DynamicBrakeForceCurves == null && MaxDynamicBrakeForceN > 0)
            {
                DynamicBrakeForceCurves = new Interpolator2D(2);
                Interpolator interp = new Interpolator(2);
                interp[0] = 0;
                interp[100] = 0;
                DynamicBrakeForceCurves[0] = interp;
                interp = new Interpolator(4);
                interp[DynamicBrakeSpeed1] = 0;
                interp[DynamicBrakeSpeed2] = MaxDynamicBrakeForceN;
                interp[DynamicBrakeSpeed3] = MaxDynamicBrakeForceN;
                interp[DynamicBrakeSpeed4] = 0;
                DynamicBrakeForceCurves[1] = interp;
            }
        }

        /// <summary>
        /// Parse the wag file parameters required for the simulator and viewer classes
        /// </summary>
        public override void Parse(string lowercasetoken, STFReader f)
        {
            if (lowercasetoken.StartsWith("engine(trainbrakescontroller"))
                TrainBrakeController.ParseBrakeValue(lowercasetoken.Substring(28), f);
            if (lowercasetoken.StartsWith("engine(enginebrakescontroller"))
                EngineBrakeController.ParseBrakeValue(lowercasetoken.Substring(29), f);
            switch (lowercasetoken)
            {
                case "engine(sound": CabSoundFileName = f.ReadStringBlock(); break;
                case "engine(cabview": CVFFileName = f.ReadStringBlock(); break;
                case "engine(maxpower": MaxPowerW = ParseW(f.ReadStringBlock(),f); break;
                case "engine(maxforce": MaxForceN = ParseN(f.ReadStringBlock(),f); break;
                case "engine(maxcontinuousforce": MaxContinuousForceN = ParseN(f.ReadStringBlock(), f); break;
                case "engine(maxvelocity": MaxSpeedMpS = ParseMpS(f.ReadStringBlock(),f); break;
                case "engine(enginecontrollers(throttle": ThrottleController = new MSTSNotchController(f); break;
                case "engine(enginecontrollers(regulator": ThrottleController = new MSTSNotchController(f); break;
                case "engine(enginecontrollers(brake_train": TrainBrakeController.Parse(f); break;
                case "engine(enginecontrollers(brake_engine": EngineBrakeController.Parse(f); break;
                case "engine(enginecontrollers(brake_dynamic": DynamicBrakeController.Parse(f); break;
                case "engine(airbrakesmainresvolume": MainResVolumeFT3 = f.ReadFloatBlock(); break;
                case "engine(airbrakesmainmaxairpressure": MainResPressurePSI = MaxMainResPressurePSI = f.ReadFloatBlock(); break;
                case "engine(airbrakescompressorrestartpressure": CompressorRestartPressurePSI = f.ReadFloatBlock(); break;
                case "engine(mainreschargingrate": MainResChargingRatePSIpS = f.ReadFloatBlock(); break;
                case "engine(enginebrakereleaserate": EngineBrakeReleaseRatePSIpS = f.ReadFloatBlock(); break;
                case "engine(enginebrakeapplicationrate": EngineBrakeApplyRatePSIpS = f.ReadFloatBlock(); break;
                case "engine(brakepipetimefactor": BrakePipeTimeFactorS = f.ReadFloatBlock(); break;
                case "engine(brakeservicetimefactor": BrakeServiceTimeFactorS = f.ReadFloatBlock(); break;
                case "engine(brakeemergencytimefactor": BrakeEmergencyTimeFactorS = f.ReadFloatBlock(); break;
                case "engine(brakepipechargingrate": BrakePipeChargingRatePSIpS = f.ReadFloatBlock(); break;
                case "engine(maxtractiveforcecurves": TractiveForceCurves = new Interpolator2D(f); break;
                case "engine(dynamicbrakeforcecurves": DynamicBrakeForceCurves = new Interpolator2D(f); break;
                case "engine(dynamicbrakesminusablespeed": DynamicBrakeSpeed1 = f.ReadFloatBlock(); break;
                case "engine(dynamicbrakesfadingspeed": DynamicBrakeSpeed2 = f.ReadFloatBlock(); break;
                case "engine(dynamicbrakesmaximumeffectivespeed": DynamicBrakeSpeed3 = f.ReadFloatBlock(); break;
                case "engine(dynamicbrakesmaximumspeedforfadeout": DynamicBrakeSpeed4 = f.ReadFloatBlock(); break;
                case "engine(dynamicbrakesmaximumforce": MaxDynamicBrakeForceN = f.ReadFloatBlock(); break;
                case "engine(dynamicbrakeshasautobailoff": DynamicBrakeAutoBailOff = f.ReadBoolBlock(); break;
                case "engine(continuousforcetimefactor": ContinuousForceTimeFactor = f.ReadFloatBlock(); break;
                default: base.Parse(lowercasetoken, f); break;
            }
        }

        /// <summary>
        /// This initializer is called when we are making a new copy of a car already
        /// loaded in memory.  We use this one to speed up loading by eliminating the
        /// need to parse the wag file multiple times.
        /// </summary>
        public override void InitializeFromCopy(MSTSWagon copy)
        {
            MSTSLocomotive locoCopy = (MSTSLocomotive)copy;
            CabSoundFileName = locoCopy.CabSoundFileName;
            CVFFileName = locoCopy.CVFFileName;
            CVFFile = locoCopy.CVFFile;
            MaxPowerW = locoCopy.MaxPowerW;
            MaxForceN = locoCopy.MaxForceN;
            MaxSpeedMpS = locoCopy.MaxSpeedMpS;
            TractiveForceCurves = locoCopy.TractiveForceCurves;
            MaxContinuousForceN = locoCopy.MaxContinuousForceN;
            ContinuousForceTimeFactor = locoCopy.ContinuousForceTimeFactor;
            DynamicBrakeForceCurves = locoCopy.DynamicBrakeForceCurves;
            DynamicBrakeAutoBailOff = locoCopy.DynamicBrakeAutoBailOff;

            IsDriveable = copy.IsDriveable;
            //ThrottleController = MSTSEngineController.Copy(locoCopy.ThrottleController);
            ThrottleController = (MSTSNotchController)locoCopy.ThrottleController.Clone();
            TrainBrakeController = (MSTSBrakeController)locoCopy.TrainBrakeController.Clone();
            EngineBrakeController = locoCopy.EngineBrakeController != null ? (MSTSBrakeController)locoCopy.EngineBrakeController.Clone() : null;
            DynamicBrakeController = locoCopy.DynamicBrakeController != null ? (MSTSNotchController)locoCopy.DynamicBrakeController.Clone() : null;

            base.InitializeFromCopy(copy);  // each derived level initializes its own variables
        }

        /// <summary>
        /// We are saving the game.  Save anything that we'll need to restore the 
        /// status later.
        /// </summary>
        public override void Save(BinaryWriter outf)
        {
            // we won't save the horn state
            outf.Write(Bell);
            outf.Write(Sander);
            outf.Write(Wiper);
            outf.Write(MainResPressurePSI);
            outf.Write(CompressorOn);
            outf.Write(AverageForceN);
            ControllerFactory.Save(ThrottleController, outf);
            ControllerFactory.Save(TrainBrakeController, outf);
            ControllerFactory.Save(EngineBrakeController, outf);
            ControllerFactory.Save(DynamicBrakeController, outf);            
            base.Save(outf);
        }

        /// <summary>
        /// We are restoring a saved game.  The TrainCar class has already
        /// been initialized.   Restore the game state.
        /// </summary>
        public override void Restore(BinaryReader inf)
        {
            if (inf.ReadBoolean()) SignalEvent(EventID.BellOn);
            if (inf.ReadBoolean()) SignalEvent(EventID.SanderOn);
            if (inf.ReadBoolean()) SignalEvent(EventID.WiperOn);
            MainResPressurePSI = inf.ReadSingle();
            CompressorOn = inf.ReadBoolean();
            AverageForceN = inf.ReadSingle();
            ThrottleController = (MSTSNotchController)ControllerFactory.Restore(inf);
            TrainBrakeController = (MSTSBrakeController)ControllerFactory.Restore(inf);
            EngineBrakeController = (MSTSBrakeController)ControllerFactory.Restore(inf);
            DynamicBrakeController = (MSTSNotchController)ControllerFactory.Restore(inf);
            base.Restore(inf);
        }

        public bool IsLeadLocomotive()
        {
            return Train.LeadLocomotive == this;
        }


        /// <summary>
        /// Create a viewer for this locomotive.   Viewers are only attached
        /// while the locomotive is in viewing range.
        /// </summary>
        public override TrainCarViewer GetViewer(Viewer3D viewer)
        {
            return new MSTSLocomotiveViewer(viewer, this);
        }

        /// <summary>
        /// This is a periodic update to calculate physics 
        /// parameters and update the base class's MotiveForceN 
        /// and FrictionForceN values based on throttle settings
        /// etc for the locomotive.
        /// </summary>
        public override void Update(float elapsedClockSeconds)
        {
            TrainBrakeController.Update(elapsedClockSeconds);
            if (EngineBrakeController != null)
                EngineBrakeController.Update(elapsedClockSeconds);

            if ((DynamicBrakeController != null) && (DynamicBrakePercent >= 0))
            {
                if (this.IsLeadLocomotive())
                    DynamicBrakePercent = DynamicBrakeController.Update(elapsedClockSeconds) * 100.0f;
                else
                    DynamicBrakeController.Update(elapsedClockSeconds);
            }

            //Currently the ThrottlePercent is global to the entire train
            //So only the lead locomotive updates it, the others only updates the controller (actually useless)
            if (this.IsLeadLocomotive())
                ThrottlePercent = ThrottleController.Update(elapsedClockSeconds) * 100.0f;
            else
                ThrottleController.Update(elapsedClockSeconds);

            // TODO  this is a wild simplification for electric and diesel electric
            float t = ThrottlePercent / 100f;
            float currentSpeedMpS = Math.Abs(SpeedMpS);
            if (TractiveForceCurves == null)
            {
                float maxForceN = MaxForceN * t;
                float maxPowerW = MaxPowerW * t * t;
                if (maxForceN * currentSpeedMpS > maxPowerW)
                    maxForceN = maxPowerW / currentSpeedMpS;
                if (currentSpeedMpS > MaxSpeedMpS)
                    maxForceN= 0;
                MotiveForceN = maxForceN;
            }
            else
            {
                MotiveForceN = TractiveForceCurves.Get(t, currentSpeedMpS);
                if (MotiveForceN < 0)
                    MotiveForceN = 0;
            }

            MotiveForceN *= 1 - (MaxForceN - MaxContinuousForceN) / (MaxForceN * MaxContinuousForceN) * AverageForceN;
            float w = (ContinuousForceTimeFactor - elapsedClockSeconds) / ContinuousForceTimeFactor;
            if (w < 0)
                w = 0;
            AverageForceN = w * AverageForceN + (1 - w) * MotiveForceN;
            MotiveForceN *= (Direction == Direction.Forward ? 1 : -1);

            // Variable1 is wheel rotation in m/sec for steam locomotives
            //Variable2 = Math.Abs(MotiveForceN) / MaxForceN;   // force generated
            Variable1 = ThrottlePercent / 100f;   // throttle setting

            if (DynamicBrakePercent > 0 && DynamicBrakeForceCurves != null)
            {
                float f= DynamicBrakeForceCurves.Get(.01f * DynamicBrakePercent, currentSpeedMpS);
                if (f > 0)
                    MotiveForceN -= (SpeedMpS > 0 ? 1 : -1) * f;
            }

            if (MainResPressurePSI < CompressorRestartPressurePSI && !CompressorOn)
                SignalEvent(EventID.CompressorOn);
            else if (MainResPressurePSI > MaxMainResPressurePSI && CompressorOn)
                SignalEvent(EventID.CompressorOff);
            if (CompressorOn)
                MainResPressurePSI += elapsedClockSeconds * MainResChargingRatePSIpS;

            base.Update(elapsedClockSeconds);
        }

        public void SetDirection( Direction direction )
        {
            // Direction Control
            if ( Direction != direction && ThrottlePercent < 1)
            {
                Direction = direction;
                if (direction == Direction.Forward)
                {
                    SignalEvent(EventID.Forward);
                    Train.MUReverserPercent = 100;
                }
                else
                {
                    SignalEvent(EventID.Reverse);
                    Train.MUReverserPercent = -100;
                }
            }
        }        

        public void StartThrottleIncrease()
        {
            if (DynamicBrakePercent >= 0)
            {
                // signal sound
                return;
            }
            ThrottleController.StartIncrease();

            // By GeorgeS
            if (EventID.IsMSTSBin)
                SignalEvent(EventID.PowerHandler);
        }

        public void StopThrottleIncrease()
        {
            if (DynamicBrakePercent >= 0)
            {
                // signal sound
                return;
            }

            ThrottleController.StopIncrease();
        }

        public void StartThrottleDecrease()
        {
            if (DynamicBrakePercent >= 0)
            {
                // signal sound
                return;
            }
            ThrottleController.StartDecrease();

            // By GeorgeS
            if (EventID.IsMSTSBin)
                SignalEvent(EventID.PowerHandler);
        }

        public void StopThrottleDecrease()
        {
            if (DynamicBrakePercent >= 0)
            {
                // signal sound
                return;
            }

            ThrottleController.StopDecrease();
        }

        public void StartTrainBrakeIncrease()
        {
            TrainBrakeController.StartIncrease();
            // By GeorgeS
            if (EventID.IsMSTSBin)
                SignalEvent(EventID.TrainBrakeSet);
        }

        public void StopTrainBrakeIncrease()
        {
            TrainBrakeController.StopIncrease();
        }

        public void StartTrainBrakeDecrease()
        {
            TrainBrakeController.StartDecrease();
            // By GeorgeS
            if (EventID.IsMSTSBin)
                SignalEvent(EventID.TrainBrakeSet);
        }

        public void StopTrainBrakeDecrease()
        {
            TrainBrakeController.StopDecrease();
        }        

        public void SetEmergency()
        {           
            TrainBrakeController.SetEmergency();
            SignalEvent(EventID.TrainBrakeEmergency);
        }
        public override string GetTrainBrakeStatus()
        {            
            string s = TrainBrakeController.GetStatus();
            if (BrakeSystem.GetType() == typeof(AirSinglePipe))
                s += string.Format(" EQ {0:F0} ", Train.BrakeLine1PressurePSI);
            else
                s += string.Format(" {0:F0} ", Train.BrakeLine1PressurePSI);
            s += BrakeSystem.GetStatus(1);
            TrainCar lastCar = Train.Cars[Train.Cars.Count - 1];
            if (lastCar == this)
                lastCar = Train.Cars[0];
            if (lastCar != this)
                s = s + " " + lastCar.BrakeSystem.GetStatus(0);
            return s;
        }

        public void StartEngineBrakeIncrease()
        {
            if (EngineBrakeController == null)
                return;

            EngineBrakeController.StartIncrease();
        }

        public void StopEngineBrakeIncrease()
        {
            if (EngineBrakeController == null)
                return;

            EngineBrakeController.StopIncrease();
        }

        public void StartEngineBrakeDecrease()
        {
            if (EngineBrakeController == null)
                return;

            EngineBrakeController.StartDecrease();
        }

        public void StopEngineBrakeDecrease()
        {
            if (EngineBrakeController == null)
                return;

            EngineBrakeController.StopDecrease();
        }
  
        public override string GetEngineBrakeStatus()
        {
            if (EngineBrakeController == null)
                return null;
            return string.Format("{0}{1}", EngineBrakeController.GetStatus(), BailOff ? " BailOff" : "");
        }

        public void ToggleBailOff()
        {
            BailOff = !BailOff;
        }

        private bool CanUseDynamicBrake()
        {
            return (DynamicBrakeController != null && DynamicBrakeForceCurves != null && ThrottlePercent == 0);
        }

        public void StartDynamicBrakeIncrease()
        {
            if(!CanUseDynamicBrake())
                return;

            if (DynamicBrakePercent < 0)
            {
                //activate it
                DynamicBrakePercent = 0;
                return;
            }
            else
                DynamicBrakeController.StartIncrease();
        }

        public void StopDynamicBrakeIncrease()
        {
            if (!CanUseDynamicBrake())
                return;

            DynamicBrakeController.StopIncrease();
        }

        public void StartDynamicBrakeDecrease()
        {
            if (!CanUseDynamicBrake())
                return;

            if (DynamicBrakePercent <= 0)
                DynamicBrakePercent = -1;
            else
            {
                DynamicBrakeController.StartDecrease();
            }
        }

        public void StopDynamicBrakeDecrease()
        {
            if (!CanUseDynamicBrake())
                return;

            DynamicBrakeController.StopDecrease();
        }

        public override string GetDynamicBrakeStatus()
        {
            if (DynamicBrakeController == null || DynamicBrakePercent < 0)
                return null;
            return string.Format("{0}", DynamicBrakeController.GetStatus());
        }
        
        /// <summary>
        /// Used when someone want to notify us of an event
        /// </summary>
        public override void SignalEvent(EventID eventID)
        {
            // Modified according to replacable IDs - by GeorgeS
            //switch (eventID)
            do
            {
                if (eventID == EventID.BellOn) { Bell = true; break; }
                if (eventID == EventID.BellOff) {  Bell = false; break; }
                if (eventID == EventID.HornOn) { Horn = true; break; }
                if (eventID == EventID.HornOff) { Horn = false; break; }
                if (eventID == EventID.SanderOn) { Sander = true; break; }
                if (eventID == EventID.SanderOff) { Sander = false; break; }
                if (eventID == EventID.WiperOn) { Wiper = true; break; }
                if (eventID == EventID.WiperOff) { Wiper = false; break; }
                if (eventID == EventID.HeadlightOff) { Headlight = 0; break; }
                if (eventID == EventID.HeadlightDim) { Headlight = 1; break; }
                if (eventID == EventID.HeadlightOn) {  Headlight = 2; break; }
                if (eventID == EventID.CompressorOn) { CompressorOn = true; break; }
                if (eventID == EventID.CompressorOff) { CompressorOn = false; break; }
                if (eventID == EventID.LightSwitchToggle) { CabLightOn = !CabLightOn; break; }
            } while (false);

            base.SignalEvent(eventID );
        }

    } // LocomotiveSimulator

    ///////////////////////////////////////////////////
    ///   3D VIEW
    ///////////////////////////////////////////////////

    /// <summary>
    /// Adds animation for wipers to the basic TrainCar
    /// </summary>
    public class MSTSLocomotiveViewer : MSTSWagonViewer
    {
        MSTSLocomotive Locomotive;

        List<int> WiperPartIndexes = new List<int>();

        float WiperAnimationKey = 0;

        protected MSTSLocomotive MSTSLocomotive { get { return (MSTSLocomotive)Car; } }

        private CabRenderer _CabRenderer = null;

        public MSTSLocomotiveViewer(Viewer3D viewer, MSTSLocomotive car)
            : base(viewer, car)
        {
            Locomotive = car;

            if (car.CVFFile != null && car.CVFFile.TwoDViews.Count > 0)
                _CabRenderer = new CabRenderer(viewer, Locomotive);

            // Find the animated parts
            if (TrainCarShape.SharedShape.Animations != null)
            {
                for (int iMatrix = 0; iMatrix < TrainCarShape.SharedShape.MatrixNames.Length; ++iMatrix)
                {
                    string matrixName = TrainCarShape.SharedShape.MatrixNames[iMatrix].ToUpper();
                    switch (matrixName)
                    {
                        case "WIPERARMLEFT1":
                        case "WIPERBLADELEFT1":
                        case "WIPERARMRIGHT1":
                        case "WIPERBLADERIGHT1":
                            if (TrainCarShape.SharedShape.Animations[0].FrameCount > 1)  // ensure shape file is properly animated for wipers
                                WiperPartIndexes.Add(iMatrix);
                            break;
                        case "MIRRORARMLEFT1":
                        case "MIRRORLEFT1":
                        case "MIRRORARMRIGHT1":
                        case "MIRRORRIGHT1":
                            // TODO
                            break;
                    }
                }
            }

            string wagonFolderSlash = Path.GetDirectoryName(Locomotive.WagFilePath) + "\\";
            if (Locomotive.CabSoundFileName != null) LoadCarSound(wagonFolderSlash, Locomotive.CabSoundFileName);

        }

        /// <summary>
        /// A keyboard or mouse click has occurred. Read the UserInput
        /// structure to determine what was pressed.
        /// </summary>
        public override void HandleUserInput(ElapsedTime elapsedTime)
        {
            if (UserInput.IsPressed(Keys.W)) Locomotive.SetDirection(Direction.Forward);
            if (UserInput.IsPressed(Keys.S)) Locomotive.SetDirection(Direction.Reverse);
    
            if (UserInput.IsPressed(Keys.D)) Locomotive.StartThrottleIncrease();
            if (UserInput.IsReleased(Keys.D)) Locomotive.StopThrottleIncrease();
            if (UserInput.IsPressed(Keys.A)) Locomotive.StartThrottleDecrease();
            if (UserInput.IsReleased(Keys.A)) Locomotive.StopThrottleDecrease();

            if (UserInput.IsPressed(Keys.OemQuotes) && !UserInput.IsShiftDown()) Locomotive.StartTrainBrakeIncrease();
            if (UserInput.IsReleased(Keys.OemQuotes) && !UserInput.IsShiftDown()) Locomotive.StopTrainBrakeIncrease();
            if (UserInput.IsPressed(Keys.OemSemicolon) && !UserInput.IsShiftDown()) Locomotive.StartTrainBrakeDecrease();
            if (UserInput.IsReleased(Keys.OemSemicolon) && !UserInput.IsShiftDown()) Locomotive.StopTrainBrakeDecrease();

            if (UserInput.IsPressed(Keys.OemCloseBrackets) && !UserInput.IsShiftDown()) Locomotive.StartEngineBrakeIncrease();
            if (UserInput.IsReleased(Keys.OemCloseBrackets) && !UserInput.IsShiftDown()) Locomotive.StopEngineBrakeIncrease();
            if (UserInput.IsPressed(Keys.OemOpenBrackets) && !UserInput.IsShiftDown()) Locomotive.StartEngineBrakeDecrease();
            if (UserInput.IsReleased(Keys.OemOpenBrackets) && !UserInput.IsShiftDown()) Locomotive.StopEngineBrakeDecrease();

            if (UserInput.IsPressed(Keys.OemComma) && !UserInput.IsShiftDown()) Locomotive.StartDynamicBrakeIncrease();
            if (UserInput.IsReleased(Keys.OemComma) && !UserInput.IsShiftDown()) Locomotive.StopDynamicBrakeIncrease();
            if (UserInput.IsPressed(Keys.OemPeriod) && !UserInput.IsShiftDown()) Locomotive.StartDynamicBrakeDecrease();
            if (UserInput.IsReleased(Keys.OemPeriod) && !UserInput.IsShiftDown()) Locomotive.StopDynamicBrakeDecrease();            

            if (UserInput.IsPressed(Keys.OemQuestion) && !UserInput.IsShiftDown()) Locomotive.ToggleBailOff();            
            if (UserInput.IsPressed(Keys.OemQuestion) && UserInput.IsShiftDown()) Locomotive.Train.InitializeBrakes();
            if (UserInput.IsPressed(Keys.OemSemicolon) && UserInput.IsShiftDown()) Locomotive.Train.SetHandbrakePercent(0);
            if (UserInput.IsPressed(Keys.OemQuotes) && UserInput.IsShiftDown()) Locomotive.Train.SetHandbrakePercent(100);
            if (UserInput.IsPressed(Keys.OemOpenBrackets) && UserInput.IsShiftDown()) Locomotive.Train.SetRetainers(false);
            if (UserInput.IsPressed(Keys.OemCloseBrackets) && UserInput.IsShiftDown()) Locomotive.Train.SetRetainers(true);
            if (UserInput.IsPressed(Keys.OemPipe) && !UserInput.IsShiftDown()) Locomotive.Train.ConnectBrakeHoses();
            if (UserInput.IsPressed(Keys.OemPipe) && UserInput.IsShiftDown()) Locomotive.Train.DisconnectBrakes();
            if (UserInput.IsPressed(Keys.Back)) Locomotive.SetEmergency();
            if (UserInput.IsPressed(Keys.X)) Locomotive.Train.SignalEvent(Locomotive.Sander ? EventID.SanderOff : EventID.SanderOn); 
            if (UserInput.IsPressed(Keys.V)) Locomotive.SignalEvent(Locomotive.Wiper ? EventID.WiperOff : EventID.WiperOn);
            if (UserInput.IsKeyDown(Keys.Space) != Locomotive.Horn) Locomotive.SignalEvent(Locomotive.Horn ? EventID.HornOff : EventID.HornOn);
            if (UserInput.IsPressed(Keys.B) != Locomotive.Bell) Locomotive.SignalEvent(Locomotive.Bell ? EventID.BellOff : EventID.BellOn);
            if (UserInput.IsPressed(Keys.H) && UserInput.IsShiftDown())
            {
                switch ((Locomotive.Headlight))
                {
                    case 1: Locomotive.Headlight = 0; break;
                    case 2: Locomotive.Headlight = 1; break;
                }
                // By GeorgeS
                if (EventID.IsMSTSBin)
                    Locomotive.SignalEvent(EventID.LightSwitchToggle);
            }
            else if (UserInput.IsPressed(Keys.H))
            {
                switch ((Locomotive.Headlight))
                {
                    case 0: Locomotive.Headlight = 1; break;
                    case 1: Locomotive.Headlight = 2; break;
                }
                // By GeorgeS
                if (EventID.IsMSTSBin)
                    Locomotive.SignalEvent(EventID.LightSwitchToggle);
            }
            if (UserInput.IsPressed(Keys.Tab) && !UserInput.IsCtrlKeyDown())
                Program.Simulator.AI.Dispatcher.ExtendPlayerAuthorization();
            if (UserInput.IsPressed(Keys.Tab) && UserInput.IsCtrlKeyDown())
                Program.Simulator.AI.Dispatcher.ReleasePlayerAuthorization();

            // By GeorgeS
            if (UserInput.IsPressed(Keys.L)) Locomotive.SignalEvent(EventID.LightSwitchToggle);
            if (UserInput.IsPressed(Keys.D1) && UserInput.IsShiftDown()) Locomotive.ShowCab = !Locomotive.ShowCab;
            base.HandleUserInput( elapsedTime );
        }

        /// <summary>
        /// We are about to display a video frame.  Calculate positions for 
        /// animated objects, and add their primitives to the RenderFrame list.
        /// </summary>
        public override void PrepareFrame(RenderFrame frame, ElapsedTime elapsedTime)
        {
            float elapsedClockSeconds = elapsedTime.ClockSeconds;
            // Wiper animation
            if (WiperPartIndexes.Count > 0)  // skip this if there are no wipers
            {
                if (Locomotive.Wiper) // on
                {
                    // Wiper Animation
                    // Compute the animation key based on framerate etc
                    // ie, with 8 frames of animation, the key will advance from 0 to 8 at the specified speed.
                    WiperAnimationKey += ((float)TrainCarShape.SharedShape.Animations[0].FrameRate / 10f) * elapsedClockSeconds;
                    while (WiperAnimationKey >= TrainCarShape.SharedShape.Animations[0].FrameCount) WiperAnimationKey -= TrainCarShape.SharedShape.Animations[0].FrameCount;
                    while (WiperAnimationKey < -0.00001) WiperAnimationKey += TrainCarShape.SharedShape.Animations[0].FrameCount;
                    foreach (int iMatrix in WiperPartIndexes)
                        TrainCarShape.AnimateMatrix(iMatrix, WiperAnimationKey);
                }
                else // off
                {
                    if (WiperAnimationKey > 0.001)  // park the blades
                    {
                        WiperAnimationKey += ((float)TrainCarShape.SharedShape.Animations[0].FrameRate / 10f) * elapsedClockSeconds;
                        if (WiperAnimationKey >= TrainCarShape.SharedShape.Animations[0].FrameCount) WiperAnimationKey = 0;
                        foreach (int iMatrix in WiperPartIndexes)
                            TrainCarShape.AnimateMatrix(iMatrix, WiperAnimationKey);
                    }
                }
            }

            // Draw 2D CAB View - by GeorgeS
            if (Viewer.Camera.AttachedCar == this.MSTSWagon &&
                Viewer.Camera.Style == Camera.Styles.Cab &&
                _CabRenderer != null)
                _CabRenderer.PrepareFrame(frame);
            
            base.PrepareFrame( frame, elapsedTime );
        }


        /// <summary>
        /// This doesn't function yet.
        /// </summary>
        public override void Unload()
        {
            base.Unload();
        }

    } // Class LocomotiveViewer


    // By GeorgeS
    public class CabRenderer : RenderPrimitive
    {
        private SpriteBatchMaterial _Sprite2DCabView;
        private List<Texture2D> _CabViews = new List<Texture2D>();
        private List<Texture2D> _NightViews = new List<Texture2D>();
        private List<Texture2D> _LightViews = new List<Texture2D>();
        private Rectangle _CabRect;
        private Matrix _Scale = Matrix.Identity;

        private Viewer3D _Viewer;
        private MSTSLocomotive _Locomotive;
        private int _Location;
        private bool _Dark = false;
        private bool _CabLight = false;

        public CabRenderer(Viewer3D viewer, MSTSLocomotive car)
        {
			//Sequence = RenderPrimitiveSequence.CabView;
            _Sprite2DCabView = new SpriteBatchMaterial(viewer.RenderProcess);

            // Loading ACE files, skip displaying ERROR messages
            foreach (string cabfile in car.CVFFile.TwoDViews)
            {
                if (File.Exists(cabfile))
                    _CabViews.Add(SharedTextureManager.Get(viewer.GraphicsDevice, cabfile));
                else
                    _CabViews.Add(Materials.MissingTexture);
            }

            foreach (string cabfile in car.CVFFile.NightViews)
                if (File.Exists(cabfile))
                    _NightViews.Add(SharedTextureManager.Get(viewer.GraphicsDevice, cabfile));
                else
                    _NightViews.Add(Materials.MissingTexture);

            foreach (string cabfile in car.CVFFile.LightViews)
                if (File.Exists(cabfile))
                    _LightViews.Add(SharedTextureManager.Get(viewer.GraphicsDevice, cabfile));
                else
                    _LightViews.Add(Materials.MissingTexture);

            _Viewer = viewer;
            _Locomotive = car;
        }

        public void PrepareFrame(RenderFrame frame)
        {
            if (!_Locomotive.ShowCab)
                return;

            CabCamera cbc = _Viewer.Camera as CabCamera;
            if (cbc != null)
            {
                _Location = cbc.SideLocation;
            }
            else
            {
                _Location = 0;
            }

            // Night
            // TODO set tunnels
            _Dark = Materials.sunDirection.Y <= 0f;
            _CabLight = _Locomotive.CabLightOn;

            _CabRect = new Rectangle(0, 0, (int)_Viewer.DisplaySize.X, (int)_Viewer.DisplaySize.Y);
            frame.AddPrimitive(_Sprite2DCabView, this, RenderPrimitiveGroup.Cab, ref _Scale);
        }
        
        public override void Draw(GraphicsDevice graphicsDevice)
        {
            Texture2D cabv = Materials.MissingTexture;

            // Try to find the right texture to draw
            if (_Dark)
            {
                if (_CabLight)
                {
                    cabv = _LightViews[_Location];
                }
                
                if (cabv == Materials.MissingTexture)
                {
                    cabv = _NightViews[_Location];
                }
            }
            
            if (cabv == Materials.MissingTexture)
            {
                cabv = _CabViews[_Location];
            }

            _Sprite2DCabView.SpriteBatch.Draw(cabv, _CabRect, Color.White);
        }
    }

}
