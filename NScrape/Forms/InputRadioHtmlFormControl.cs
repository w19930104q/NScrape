using System.Collections.Generic;

namespace NScrape.Forms;

/// <summary>
/// Represents an HTML <b>input radio</b> control.
/// </summary>
public class InputRadioHtmlFormControl : InputCheckableHtmlFormControl {
	private IReadOnlyList<InputRadioHtmlFormControl> group;

	/// <summary>
	/// Initializes a new instance of the <see cref="InputRadioHtmlFormControl"/> class.
	/// </summary>
	/// <param name="html">Contains the control HTML.</param>
	public InputRadioHtmlFormControl( string html )
		: base( html ) {
	}

	/// <summary>
	/// Gets or sets whether or not the control is checked.
	/// </summary>
	/// <remarks>
	/// Radio buttons are mutually exclusive within their form and <b>name</b>: checking this
	/// control atomically unchecks the other radio controls in its group.
	/// </remarks>
	public override bool Checked {
		get => base.Checked;
		set {
			if ( value ) {
				UncheckGroup();
			}

			base.Checked = value;
		}
	}

	/// <summary>
	/// Sets the radio group (the radio controls sharing this control's <b>name</b> within the same form).
	/// </summary>
	/// <param name="radioGroup">Contains the radio controls in the group, including this control.</param>
	internal void SetGroup( IReadOnlyList<InputRadioHtmlFormControl> radioGroup ) {
		group = radioGroup;
	}

	private void UncheckGroup() {
		if ( group == null ) {
			return;
		}

		foreach ( var radio in group ) {
			if ( !ReferenceEquals( radio, this ) ) {
				radio.Checked = false;
			}
		}
	}
}
