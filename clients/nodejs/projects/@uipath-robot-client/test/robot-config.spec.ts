import { RobotConfig, IRobotEnvironment } from '../src/robot-config';

describe(`RobotEnvironment`, () => {

    test(`basic usage`, () => {
        expect(() => RobotConfig.data).not.toThrow();
    });

    test(`env vars not set, UiPath.Service.UserHost.exe doesn't exist => Installed Service`, () => {
        const mock: IRobotEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
            userName: '',
            userDomain: '',

            getVar: jest.fn(),
            fileExists: jest.fn()
        };
        RobotConfig.environment = mock;
        RobotConfig.filenameUserHostService = 'UiPath.Service.UserHost.exe';
        RobotConfig.relpathFailoverServiceHome = '..\\..';

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).toHaveBeenCalled();

        expect(RobotConfig.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotConfig.data.serviceInstalled).toBe(true);
    });

    test(`env vars not set, UiPath.Service.UserHost.exe exists => Service not installed`, () => {
        const mock: IRobotEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
            userName: '',
            userDomain: '',

            getVar: jest.fn(),
            fileExists: jest.fn(x => {
                switch (x) {
                    case 'C:\\Program Files (x86)\\UiPath\\UiPath.Service.UserHost.exe':
                        return true;
                    default:
                        return false;
                }
            }),
        };
        RobotConfig.environment = mock;
        RobotConfig.filenameUserHostService = 'UiPath.Service.UserHost.exe';
        RobotConfig.relpathFailoverServiceHome = '..\\..';

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).toHaveBeenCalled();

        expect(RobotConfig.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotConfig.data.serviceInstalled).toBe(false);
    });

    test(`env vars set but inconsistent, UiPath.Service.UserHost.exe exists => Service not installed`, () => {
        const mock: IRobotEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
            userName: '',
            userDomain: '',

            getVar: jest.fn(x => {
                switch (x) {
                    case 'foo': return 'neither true nor false';
                }
            }),
            fileExists: jest.fn(x => {
                switch (x) {
                    case 'C:\\Program Files (x86)\\UiPath\\UiPath.Service.UserHost.exe':
                        return true;
                    default:
                        return false;
                }
            }),
        };

        RobotConfig.environment = mock;
        RobotConfig.envvarnameServiceInstalled = 'foo';
        RobotConfig.filenameUserHostService = 'UiPath.Service.UserHost.exe';
        RobotConfig.relpathFailoverServiceHome = '..\\..';

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).toHaveBeenCalled();

        expect(RobotConfig.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotConfig.data.serviceInstalled).toBe(false);
    });

    test(`env vars set but inconsistent, UiPath.Service.UserHost.exe doesn't exist => Installed Service`, () => {
        const mock: IRobotEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
            userName: '',
            userDomain: '',

            getVar: jest.fn(x => {
                switch (x) {
                    case 'foo': return 'neither true nor false';
                }
            }),
            fileExists: jest.fn(),
        };
        RobotConfig.environment = mock;
        RobotConfig.envvarnameServiceInstalled = 'foo';
        RobotConfig.filenameUserHostService = 'UiPath.Service.UserHost.exe';
        RobotConfig.relpathFailoverServiceHome = '..\\..';

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).toHaveBeenCalled();

        expect(RobotConfig.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotConfig.data.serviceInstalled).toBe(true);
    });

    test(`UiPath.Service.UserHost.exe exists BUT env var states service is installed => Installed Service`, () => {
        const mock: IRobotEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
            userName: '',
            userDomain: '',

            getVar: jest.fn(x => {
                switch (x) {
                    case 'foo': return 'tRUe';
                }
            }),
            fileExists: jest.fn(x => {
                switch (x) {
                    case 'C:\\Program Files (x86)\\UiPath\\UiPath.Service.UserHost.exe':
                        return true;
                    default:
                        return false;
                }
            }),
        };
        RobotConfig.environment = mock;
        RobotConfig.envvarnameServiceInstalled = 'foo';
        RobotConfig.filenameUserHostService = 'UiPath.Service.UserHost.exe';
        RobotConfig.relpathFailoverServiceHome = '..\\..';

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).not.toHaveBeenCalled();

        expect(RobotConfig.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotConfig.data.serviceInstalled).toBe(true);
    });

    test(`UiPath.Service.UserHost.exe doesn't exist BUT env var states service is not installed => Service not installed`, () => {
        const mock: IRobotEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
            userName: '',
            userDomain: '',

            getVar: jest.fn(x => {
                switch (x) {
                    case 'foo': return 'faLSE';
                }
            }),
            fileExists: jest.fn(x => {
                switch (x) {
                    case 'C:\\Program Files (x86)\\UiPath\\UiPath.Service.UserHost.exe':
                        return true;
                    default:
                        return false;
                }
            }),
        };
        RobotConfig.environment = mock;
        RobotConfig.envvarnameServiceInstalled = 'foo';
        RobotConfig.filenameUserHostService = 'UiPath.Service.UserHost.exe';
        RobotConfig.relpathFailoverServiceHome = '..\\..';

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).not.toHaveBeenCalled();

        expect(RobotConfig.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotConfig.data.serviceInstalled).toBe(false);
    });

});
