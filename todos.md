# TODOs

## wrap up the project

- revise session abstraction
- revise what concludes user session
- determine user/session lifecycle (when to create flows)
- design API
- implement API
* client MVP

## authorization implementation plan

* check if authorized, redirect to login page
* logon page/google/fb
* signin: choose nick form/step
* signin: connect/register user
* logout step

## ideas

* [x] store internally uuid and channels for the user, let application specific user info be a parameter to chat
* [] store ActorSystem in server state (simplify ServerApi then)
* [] reimplement echo actor using Flow<>
* [] chan name is sufficiently good identifier, consider removing channel id in favor of name
* what if Channel, ChatServer and user session are not stores but the streams/flows to process the data.
* Keep track of disconnected users in channel vs separate channel descriptor (which keeps track of who's online).
