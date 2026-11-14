using System;
using System.Linq;
using NScrape.Forms;
using NScrape.Interfaces;
using NScrape.Requests;
using NScrape.Responses;
using NSubstitute;
using Xunit;
using WebRequest = NScrape.Requests.WebRequest;

namespace NScrape.Test.Forms;

/// <summary>
/// Offline tests for <see cref="BasicHtmlForm"/> covering form reloading, GET/POST submission,
/// radio button grouping and legacy regressions. All fixtures are inline HTML; no network access
/// is required.
/// </summary>
public class BasicHtmlFormReloadTests {
	private static readonly Uri FormPageUrl = new( "http://example.com/formpage" );

	private const string GetFormHtml = """
		<html><body>
		<form name="search" method="get" action="/search?page=2&sort=asc">
			<input type="text" name="q" value="" />
			<input type="submit" name="go" value="Go" />
		</form>
		</body></html>
		""";

	private const string PostFormHtml = """
		<html><body>
		<form name="post" method="post" action="/submit?mode=full">
			<input type="text" name="title" value="default" />
			<input type="text" name="ignored" value="nope" disabled="disabled" />
			<input type="submit" name="btnA" value="A" />
			<input type="submit" name="btnB" value="B" />
		</form>
		</body></html>
		""";

	private const string RadioFormHtml = """
		<html><body>
		<form name="colors" method="post" action="/choose">
			<input type="radio" name="color" value="red" checked="checked" />
			<input type="radio" name="color" value="blue" />
			<input type="radio" name="size" value="small" checked="checked" />
			<input type="radio" name="size" value="large" />
			<input type="submit" name="go" value="Go" />
		</form>
		</body></html>
		""";

	private const string DoubleCheckedRadioFormHtml = """
		<html><body>
		<form name="colors" method="post" action="/choose">
			<input type="radio" name="color" value="red" checked="checked" />
			<input type="radio" name="color" value="blue" checked="checked" />
		</form>
		</body></html>
		""";

	private const string TwoFormsHtml = """
		<html><body>
		<form name="first" method="post" action="/first">
			<input type="text" name="shared" value="first-default" />
		</form>
		<form name="second" method="post" action="/second">
			<input type="text" name="shared" value="second-default" />
		</form>
		</body></html>
		""";

	private const string AspxFormHtml = """
		<html><body>
		<form name="ctl00" method="post" action="checklicense.aspx" id="ctl00">
			<input type="hidden" name="__EVENTTARGET" id="__EVENTTARGET" value="" />
			<input type="hidden" name="__EVENTARGUMENT" id="__EVENTARGUMENT" value="" />
			<input type="hidden" name="__VIEWSTATE" id="__VIEWSTATE" value="viewstate-value" />
			<input type="text" name="ctl00$MainContent$LicNo" value="" />
			<input type="submit" name="ctl00$MainContent$Search" value="Search" />
		</form>
		<script type="text/javascript">
		//<![CDATA[
		var theForm = document.forms['ctl00'];
		function __doPostBack(eventTarget, eventArgument) {
		    theForm.__EVENTTARGET.value = eventTarget.split("$").join(":");
		    theForm.__EVENTARGUMENT.value = eventArgument;
		    theForm.submit();
		}
		//]]>
		</script>
		</body></html>
		""";

	private class TestableHtmlForm : BasicHtmlForm {
		public TestableHtmlForm( IWebClient webClient )
			: base( webClient ) {
		}

		public string BuildRequestData( string submitButtonName = null ) {
			return BuildRequest( submitButtonName );
		}
	}

	private static IWebClient CreateWebClient( out Func<WebRequest> sentRequest ) {
		var webClient = Substitute.For<IWebClient>();

		WebRequest captured = null;

		var mockResponse = Substitute.For<IHtmlWebResponse>();
		mockResponse.Success.Returns( true );
		mockResponse.ResponseType.Returns( WebResponseType.Html );
		mockResponse.ResponseUrl.Returns( FormPageUrl );
		mockResponse.Html.Returns( "<html><body>ok</body></html>" );

		webClient.SendRequest( Arg.Any<WebRequest>() ).Returns( callInfo => {
			captured = callInfo.Arg<WebRequest>();
			return mockResponse;
		} );

		sentRequest = () => captured;
		return webClient;
	}

	private static string Decode( string data ) {
		return Uri.UnescapeDataString( data.Replace( "+", " " ) );
	}

	[Fact]
	public void Reload_ReplacesPreviousFormControls() {
		var webClient = CreateWebClient( out _ );

		const string firstHtml = """
			<html><body>
			<form name="a" method="post" action="/a">
				<input type="text" name="first" value="one" />
				<input type="checkbox" name="check" value="yes" checked="checked" />
			</form>
			</body></html>
			""";

		const string secondHtml = """
			<html><body>
			<form name="b" method="post" action="/b">
				<input type="text" name="second" value="two" />
			</form>
			</body></html>
			""";

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, firstHtml );

		// Prime the cached control collections and dirty some state.
		Assert.Equal( 2, form.AllControls.Count() );
		form.InputControls.Single( c => c.Name == "first" ).Value = "dirty";

		form.Load( FormPageUrl, secondHtml );

		var control = Assert.Single( form.AllControls );
		Assert.Equal( "second", control.Name );
		Assert.Equal( "two", form.InputControls.Single().Value );
		Assert.Empty( form.CheckBoxControls );
		Assert.Equal( "second=two", form.BuildRequestData() );
	}

	[Fact]
	public void Reload_ReplacesFormAttributes() {
		var webClient = CreateWebClient( out var sentRequest );

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, PostFormHtml );
		form.Load( FormPageUrl, GetFormHtml );

		using ( form.Submit() ) {
		}

		// The second form is a GET form; a POST here would mean stale attributes survived the reload.
		var request = Assert.IsType<GetWebRequest>( sentRequest() );
		Assert.Equal( "/search", request.Destination.AbsolutePath );
	}

	[Fact]
	public void Reload_DuplicateFormsOnSamePage_DoNotAccumulateControls() {
		var webClient = CreateWebClient( out _ );

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, TwoFormsHtml, 0 );
		form.InputControls.Single( c => c.Name == "shared" ).Value = "edited";

		form.Load( FormPageUrl, TwoFormsHtml, 1 );

		var control = Assert.Single( form.InputControls );
		Assert.Equal( "shared", control.Name );

		// The edit made to the first form must not leak into the second.
		Assert.Equal( "second-default", control.Value );
		Assert.Equal( "shared=second-default", form.BuildRequestData() );
	}

	[Fact]
	public void Reload_ByAttribute_ReplacesPreviousForm() {
		var webClient = CreateWebClient( out _ );

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, TwoFormsHtml, "name", "first" );
		form.Load( FormPageUrl, TwoFormsHtml, "name", "second" );

		Assert.Equal( "shared=second-default", form.BuildRequestData() );
	}

	[Fact]
	public void GetSubmit_MergesActionQueryStringWithFormData() {
		var webClient = CreateWebClient( out var sentRequest );

		var form = new BasicHtmlForm( webClient );
		form.Load( FormPageUrl, GetFormHtml );
		form.InputControls.Single( c => c.Name == "q" ).Value = "hello world";

		using ( form.Submit() ) {
		}

		var request = Assert.IsType<GetWebRequest>( sentRequest() );
		Assert.Equal( "/search", request.Destination.AbsolutePath );

		var query = Decode( request.Destination.Query );
		Assert.Contains( "page=2", query );
		Assert.Contains( "sort=asc", query );
		Assert.Contains( "q=hello world", query );
	}

	[Fact]
	public void GetSubmit_ActionWithoutQueryString_IsUnchanged() {
		var webClient = CreateWebClient( out var sentRequest );

		const string html = """
			<html><body>
			<form name="f" method="get" action="/plain">
				<input type="text" name="q" value="abc" />
			</form>
			</body></html>
			""";

		var form = new BasicHtmlForm( webClient );
		form.Load( FormPageUrl, html );

		using ( form.Submit() ) {
		}

		var request = Assert.IsType<GetWebRequest>( sentRequest() );
		Assert.Equal( "/plain", request.Destination.AbsolutePath );
		Assert.Equal( "?q=abc", request.Destination.Query );
	}

	[Fact]
	public void PostSubmit_KeepsActionQueryStringInUrlAndFormDataInBody() {
		var webClient = CreateWebClient( out var sentRequest );

		var form = new BasicHtmlForm( webClient );
		form.Load( FormPageUrl, PostFormHtml );

		using ( form.Submit( "btnA" ) ) {
		}

		var request = Assert.IsType<PostWebRequest>( sentRequest() );
		Assert.Equal( "?mode=full", request.Destination.Query );

		var body = Decode( request.RequestData );
		Assert.Contains( "title=default", body );
		Assert.DoesNotContain( "mode=full", body );
	}

	[Fact]
	public void Radio_CheckingOne_UnchecksOthersInSameGroup() {
		var webClient = CreateWebClient( out _ );

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, RadioFormHtml );

		var red = form.RadioControls.Single( r => r.Value == "red" );
		var blue = form.RadioControls.Single( r => r.Value == "blue" );

		Assert.True( red.Checked );
		Assert.False( blue.Checked );

		blue.Checked = true;

		Assert.False( red.Checked );
		Assert.True( blue.Checked );

		var data = form.BuildRequestData();
		Assert.DoesNotContain( "color=red", data );
		Assert.Contains( "color=blue", data );
	}

	[Fact]
	public void Radio_GroupsWithDifferentNames_AreIndependent() {
		var webClient = CreateWebClient( out _ );

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, RadioFormHtml );

		form.RadioControls.Single( r => r.Name == "color" && r.Value == "blue" ).Checked = true;

		// Switching the color group must not disturb the size group.
		Assert.True( form.RadioControls.Single( r => r.Name == "size" && r.Value == "small" ).Checked );
		Assert.False( form.RadioControls.Single( r => r.Name == "size" && r.Value == "large" ).Checked );

		var data = form.BuildRequestData();
		Assert.Contains( "color=blue", data );
		Assert.Contains( "size=small", data );
	}

	[Fact]
	public void Radio_MultipleCheckedInHtml_LastInDocumentOrderWins() {
		var webClient = CreateWebClient( out _ );

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, DoubleCheckedRadioFormHtml );

		Assert.False( form.RadioControls.Single( r => r.Value == "red" ).Checked );
		Assert.True( form.RadioControls.Single( r => r.Value == "blue" ).Checked );
		Assert.Equal( "color=blue", form.BuildRequestData() );
	}

	[Fact]
	public void Radio_SwitchIsAtomic_SubmittedDataContainsOnlySelectedValue() {
		var webClient = CreateWebClient( out var sentRequest );

		var form = new BasicHtmlForm( webClient );
		form.Load( FormPageUrl, RadioFormHtml );
		form.RadioControls.Single( r => r.Name == "color" && r.Value == "blue" ).Checked = true;

		using ( form.Submit() ) {
		}

		var body = Assert.IsType<PostWebRequest>( sentRequest() ).RequestData;

		// Exactly one value per radio group may be submitted.
		Assert.Equal( 1, body.Split( '&' ).Count( p => p.StartsWith( "color=", StringComparison.Ordinal ) ) );
		Assert.Contains( "color=blue", body );
		Assert.DoesNotContain( "color=red", body );
	}

	[Fact]
	public void DisabledControls_AreOmittedFromRequest() {
		var webClient = CreateWebClient( out _ );

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, PostFormHtml );

		var data = form.BuildRequestData( "btnA" );
		Assert.DoesNotContain( "ignored", data );
	}

	[Fact]
	public void SubmitButtonSelection_OnlyNamedButtonIsIncluded() {
		var webClient = CreateWebClient( out _ );

		var form = new TestableHtmlForm( webClient );
		form.Load( FormPageUrl, PostFormHtml );

		var data = form.BuildRequestData( "btnB" );
		Assert.Contains( "btnB=B", data );
		Assert.DoesNotContain( "btnA", data );
	}

	[Fact]
	public void AspxPostBack_PopulatesEventTargetAndArgument() {
		var webClient = CreateWebClient( out var sentRequest );

		var form = new BasicAspxForm( webClient );
		form.Load( FormPageUrl, AspxFormHtml, "id", "ctl00" );
		form.InputControls.Single( c => c.Name == "ctl00$MainContent$LicNo" ).Value = "956855";

		using ( form.Submit( "ctl00$MainContent$Search" ) ) {
		}

		var body = Decode( Assert.IsType<PostWebRequest>( sentRequest() ).RequestData );

		// The page contains __doPostBack, so the event target is unescaped ($ becomes :).
		Assert.Contains( "__EVENTTARGET=ctl00:MainContent:Search", body );
		Assert.Contains( "__EVENTARGUMENT=", body );
		Assert.Contains( "__VIEWSTATE=viewstate-value", body );
		Assert.Contains( "ctl00$MainContent$LicNo=956855", body );

		// The submit button itself is not included in a postback.
		Assert.DoesNotContain( "ctl00$MainContent$Search=", body );
	}
}
