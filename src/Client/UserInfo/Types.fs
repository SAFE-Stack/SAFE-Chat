module UserInfo.Types

type UserInfoData = {Nick: string; UserId: string}
type UserInfo = NotLoggedIn | UserInfo of UserInfoData | Error of exn // FIXME remove error

type Msg = Update of UserInfoData | Reset | FetchError of exn
