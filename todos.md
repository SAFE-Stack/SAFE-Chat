# TODOs

## implementation plan

- client MVP
- persist ChatServer state (registers once and forever, channels are preserved)
- persist user info (id, nick, userid)
- request user Nick form (before registration is complete)


### Far plans

- alternate flow for notifications when user if offline

## authorization implementation plan

* [x] check if authorized, redirect to login page
* [x] logon page/google/fb
* signin: choose nick form/step
* [x] signin: connect/register user
* [x] logout step

## ideas

* [x] store internally uuid and channels for the user, let application specific user info be a parameter to chat
* [] store ActorSystem in server state (simplify ServerApi then)
* [] reimplement echo actor using Flow<>
* [] chan name is sufficiently good identifier, consider removing channel id in favor of name
* what if Channel, ChatServer and user session are not stores but the streams/flows to process the data.
* Keep track of disconnected users in channel vs separate channel descriptor (which keeps track of who's online).
