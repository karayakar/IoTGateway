﻿using System;
using System.Collections.Generic;
using System.Text;
using Waher.Content.Xml;

namespace Waher.Networking.XMPP.Contracts.Search
{
	/// <summary>
	/// Limits searches to items with values greater than or equal to this value.
	/// </summary>
	public class GreaterThanOrEqualToNumber : SearchFilterNumberOperand
	{
		/// <summary>
		/// Limits searches to items with values greater than or equal to this value.
		/// </summary>
		/// <param name="Value">Number value</param>
		public GreaterThanOrEqualToNumber(double Value)
			: base(Value)
		{
		}

		/// <summary>
		/// Local XML element name of string operand.
		/// </summary>
		public override string OperandName => "gte";
	}
}
