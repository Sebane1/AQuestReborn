using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;

namespace MareSynchronos.API.SignalR;

public interface IMareHub
{
    const int ApiVersion = 32;
    const string Path = "/mare";

    Task<bool> CheckClientHealth();

    Task Client_DownloadReady(Guid requestId);
    Task Client_GroupChangePermissions(GroupPermissionDto groupPermission);
    Task Client_GroupDelete(GroupDto groupDto);
    Task Client_GroupPairChangeUserInfo(GroupPairUserInfoDto userInfo);
    Task Client_GroupPairJoined(GroupPairFullInfoDto groupPairInfoDto);
    Task Client_GroupPairLeft(GroupPairDto groupPairDto);
    Task Client_GroupSendFullInfo(GroupFullInfoDto groupInfo);
    Task Client_GroupSendInfo(GroupInfoDto groupInfo);
    Task Client_ReceiveServerMessage(MessageSeverity messageSeverity, string message);
    Task Client_UpdateSystemInfo(SystemInfoDto systemInfo);
    Task Client_UserAddClientPair(UserPairDto dto);
    Task Client_UserReceiveCharacterData(OnlineUserCharaDataDto dataDto);
    Task Client_UserReceiveUploadStatus(UserDto dto);
    Task Client_UserRemoveClientPair(UserDto dto);
    Task Client_UserSendOffline(UserDto dto);
    Task Client_UserSendOnline(OnlineUserIdentDto dto);
    Task Client_UserUpdateOtherPairPermissions(UserPermissionsDto dto);
    Task Client_UpdateUserIndividualPairStatusDto(UserIndividualPairStatusDto dto);
    Task Client_UserUpdateProfile(UserDto dto);
    Task Client_UserUpdateSelfPairPermissions(UserPermissionsDto dto);
    Task Client_UserUpdateDefaultPermissions(DefaultPermissionsDto dto);
    Task Client_GroupChangeUserPairPermissions(GroupPairUserPermissionDto dto);

    Task<ConnectionDto> GetConnectionDto();

    Task GroupBanUser(GroupPairDto dto, string reason);
    Task GroupChangeGroupPermissionState(GroupPermissionDto dto);
    Task GroupChangeOwnership(GroupPairDto groupPair);
    Task<bool> GroupChangePassword(GroupPasswordDto groupPassword);
    Task GroupClear(GroupDto group);
    Task<GroupJoinDto> GroupCreate();
    Task<List<string>> GroupCreateTempInvite(GroupDto group, int amount);
    Task GroupDelete(GroupDto group);
    Task<List<BannedGroupUserDto>> GroupGetBannedUsers(GroupDto group);
    Task<GroupJoinInfoDto> GroupJoin(GroupPasswordDto passwordedGroup);
    Task<bool> GroupJoinFinalize(GroupJoinDto passwordedGroup);
    Task GroupLeave(GroupDto group);
    Task GroupRemoveUser(GroupPairDto groupPair);
    Task GroupSetUserInfo(GroupPairUserInfoDto groupPair);
    Task<List<GroupFullInfoDto>> GroupsGetAll();
    Task GroupUnbanUser(GroupPairDto groupPair);
    Task<int> GroupPrune(GroupDto group, int days, bool execute);

    Task UserAddPair(UserDto user);
    Task UserDelete();
    Task<List<OnlineUserIdentDto>> UserGetOnlinePairs(CensusDataDto? censusDataDto);
    Task<List<UserFullPairDto>> UserGetPairedClients();
    Task<UserProfileDto> UserGetProfile(UserDto dto);
    Task UserPushData(UserCharaDataMessageDto dto);
    Task UserRemovePair(UserDto userDto);
    Task UserSetProfile(UserProfileDto userDescription);
    Task UserUpdateDefaultPermissions(DefaultPermissionsDto defaultPermissionsDto);
    Task SetBulkPermissions(BulkPermissionsDto dto);
}