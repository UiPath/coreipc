import { TimeSpan } from '@uipath/ipc';

export class RobotClientSettings {
    public static installPackageRequestTimeout: TimeSpan = TimeSpan.fromMinutes(2);
}
