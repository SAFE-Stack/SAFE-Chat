module Selectors

let messageInputPanel = ".fs-message-input"
let messageInputText = ".fs-message-input input[type='text']"
let messageSendBtn = ".fs-message-input .btn:has(> i.mdi-send)"

let switchChannel name = sprintf ".fs-menu button.fs-channel:contains('%s')" name
let newChannelInput = ".fs-menu input.fs-new-channel"

let selectedChanBtn = ".fs-menu button.selected"

let userNick = ".fs-user #usernick"
let userStatus = ".fs-user #userstatus"
let userAvatar = ".fs-user .fs-avatar"

let loginNickname = "#nickname"
let loginBtn = "#login"

let channelTitle = ".fs-chat-info h1"
let channelTopic = ".fs-chat-info span"

let channelLeaveBtn = ".fs-chat-info button[title='Leave']"