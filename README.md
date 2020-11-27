# SMART on FHIR demo implementation
This specific fork of the project is intended to further expand on implementing the FHIR Smart App Launch inside a legacy desktop application (on Windows).

These types of systems are typically rich or fat clients, that directly connect to a database, or maybe through web services.
This project is not intended to demonstrate how to create a FHIR Facade onto your existing system, there are other projects that do that.
In this Smart Facade project, I leverage the [fhir-net-web-api](https://github.com/brianpos/fhir-net-web-api/tree/master-r4) [(NuGet package)](https://www.nuget.org/packages/brianpos.Fhir.R4.WebApi.Support) 
to implement the FHIR server - so if you've already used that project, this will add in the Smart App launch capability quickly.

## Introduction
This demo show cases Smart Health IT's Pediatric Growth Application, a web application using the SMART on FHIR specification to access data from the EHR

The demo consist of four components:
* FHIR Server hosted in Azure - https://smart-fhir-api.azurewebsites.net/
* OAuth Server hosted in Azure - https://smart-auth.azurewebsites.net/.well-known/openid-configuration
* SMART on FHIR compliant web application - http://examples.smarthealthit.org/growth-chart-app/
* EHR Desktop application - which will be run by you on your desktop

## How to use the demo:

Note: Step 4 and step 7 might take some time to execute if the web apps are cold started

1. Set both Solution Platform and Platform Target in Visual Studio to x86 for the EHRApp project. This step is required because we use the CefSharp browser component.
2. Start EHRApp
3. Select File -> Open -> Patient
4. In the Find who text field enter: Susan
5. Select Susan Clark with Patient Id smart-1482713, click the button Open
6. Susan Clark's Patient form is open and the EHR context is Susan Clark
7. Select Tools -> Pediatric Growth Application.
8. If a consent screen pops up, let the defaults be and press the button "Yes, allow". This will authorize the Pediatric Growth Application to access "your" data
9. You have now started a web app which is running in the context of your EHR authorized by your EHR system to use your data


## Development Notes

* Each Launch of a smart application for a patient will have it's own Launch Context, Fhir Facade instance, and Authentication API instance
* API instances aren't exposed to the HTTP layer, so there is no attach surface open outside the Legacy Application
* The cefsharp component provides the modern browser experience, without external dependencies
* The example proxy sample stuff going out to the external server kinda skips browser CORS stuff
  as the proxy directly calls the remote API, not through the browser

There are several things that your application will need to 
CORS is implemented to only permit access to it's facade by pages in the registered web app's domain (or others specifically registered for it)

Legacy EHR App
* Implementation of the Facade Model (System and Resource)
* Provide the User/Patient Context on App Launch
* Implement the Auth Verification - providing the User Identity Token

## Useful External references 

(CORS AuthProtocolHandler/SmartApplicationDetails) https://docs.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-3.1#host-filtering