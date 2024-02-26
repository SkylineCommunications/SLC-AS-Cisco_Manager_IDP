/*
****************************************************************************
*  Copyright (c) 2021,  Skyline Communications NV  All Rights Reserved.    *
****************************************************************************

By using this script, you expressly agree with the usage terms and
conditions set out below.
This script and all related materials are protected by copyrights and
other intellectual property rights that exclusively belong
to Skyline Communications.

A user license granted for this script is strictly for personal use only.
This script may not be used in any way by anyone without the prior
written consent of Skyline Communications. Any sublicensing of this
script is forbidden.

Any modifications to this script by the user are only allowed for
personal use and within the intended purpose of the script,
and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or
malfunctions whatsoever of the script resulting from a modification
or adaptation by the user.

The content of this script is confidential information.
The user hereby agrees to keep this confidential information strictly
secret and confidential and not to disclose or reveal it, in whole
or in part, directly or indirectly to any person, entity, organization
or administration without the prior written consent of
Skyline Communications.

Any inquiries can be addressed to:

	Skyline Communications NV
	Ambachtenstraat 33
	B-8870 Izegem
	Belgium
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

****************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS
11/04/2022	1.0.0.1		ADK, Skyline	Initial version

*/

using System;
using System.Diagnostics;
using System.Threading;

using Skyline.DataMiner.Automation;
using Skyline.DataMiner.DataMinerSolutions.DataMinerSystem;
using Skyline.DataMiner.DataMinerSolutions.IDP.Software;

/// <summary>
/// DataMiner Script Class.
/// </summary>
public class Script
{
	private SoftwareUpdate softwareUpdate;

	public static Skyline.DataMiner.Automation.Element Element { get; set; }

	public static string Version { get; set; }

	/// <summary>
	/// The Script entry point.
	/// </summary>
	/// <param name="engine">Link with SLAutomation process.</param>
	public void Run(IEngine engine)
	{
		try
		{
			softwareUpdate = new SoftwareUpdate(engine);
			softwareUpdate.NotifyProcessStarted();

			PerformUpgrade(engine);
		}
		catch (ScriptAbortException)
		{
			softwareUpdate?.NotifyProcessFailure("Script aborted");
			throw;
		}
		catch (Exception e)
		{
			softwareUpdate?.NotifyProcessFailure(e.ToString());
			engine.ExitFail($"Exception thrown{Environment.NewLine}{e}");
		}
	}

	private static bool CheckDifferentVersion()
	{
		return Convert.ToString(Element.GetParameter(5)) != Version;
	}

	private static bool IsElementStateNotTimeout()
	{
		return !Convert.ToInt16(Element.GetParameter(65008)).Equals(7);
	}

	private static bool IsElementStateTimeout()
	{
		return Convert.ToInt16(Element.GetParameter(65008)).Equals(7);
	}

	private static bool Retry(Func<bool> func, TimeSpan timeout)
	{
		bool success;

		Stopwatch sw = Stopwatch.StartNew();

		do
		{
			success = func();
			if (!success)
			{
				Thread.Sleep(100);
			}
		}
		while (!success && sw.Elapsed <= timeout);

		return success;
	}

	private void PerformUpgrade(IEngine engine)
	{
		InputData inputParameters = softwareUpdate.InputData;
		IElement element = inputParameters.Element;

		IActionableElement dataMinerElement = engine.FindElement(element.AgentId, element.ElementId);

		PushUpgradeToDevice(dataMinerElement, inputParameters.ImageFileLocation);

		ValidateResult(engine);
	}

	private void PushUpgradeToDevice(IActionableElement element, string imageFileLocation)
	{
		try
		{
			Version = Convert.ToString(element.GetParameter(5));
			element.SetParameter(9901008, imageFileLocation);
			element.SetParameter(9901007, 1);
		}
		catch (Exception e)
		{
			softwareUpdate.NotifyProcessFailure(
				$"Failed to issue software update command to element{Environment.NewLine}{e}");
		}
	}

	private void ValidateResult(IEngine engine)
	{
		string deviceResult = null;
		bool result = Retry(
			() =>
			{
				if (Retry(IsElementStateTimeout, TimeSpan.FromMinutes(5)))
				{
					if (Retry(IsElementStateNotTimeout, TimeSpan.FromMinutes(5)))
					{
						return true;
					}
					else
					{
						engine.GenerateInformation("Could not perform software update to the device.");
						return false;
					}
				}
				else
				{
					engine.GenerateInformation("Could not perform software update to the device.");
					return false;
				}
			},
			TimeSpan.FromMinutes(15));

		if (!result)
		{
			throw new TimeoutException(
				$"Could not validate new software version{Environment.NewLine}Last retrieved value '{deviceResult}'");
		}

		Element.SetParameter(9901007, 0);
		Element.SetParameter(9901008, string.Empty);

		if (Retry(CheckDifferentVersion, TimeSpan.FromSeconds(10)))
		{
			softwareUpdate.NotifyProcessSuccess();
		}
		else
		{
			softwareUpdate.NotifyProcessFailure("Failed to update");
		}
	}
}