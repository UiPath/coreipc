import { RobotEnvironmentStore, IEnvironment } from '../src/robot-environment';

describe(`RobotEnvironment`, () => {

    test(`basic usage`, () => {
        // Accessing `data` before calling `initialize` should throw
        expect(() => RobotEnvironmentStore.data).toThrow();

        // Calling `initialize` should not throw
        expect(() => RobotEnvironmentStore.initialize()).not.toThrow();

        // Accessing `data` after calling `initialize` should not throw
        expect(() => RobotEnvironmentStore.data).not.toThrow();
    });

    test(`physical`, () => {
        RobotEnvironmentStore.initialize();
        expect(RobotEnvironmentStore.data.serviceInstalled).toBe(false);
    });

    test(`env vars not set, UiPath.Service.UserHost.exe doesn't exist => Installed Service`, () => {
        const mock: IEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
            getVar: jest.fn(),
            fileExists: jest.fn(),
        };
        RobotEnvironmentStore.initialize({
            environment: mock,
            filenameUserHostService: 'UiPath.Service.UserHost.exe',
            relpathFailoverServiceHome: '..\\..'
        });

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).toHaveBeenCalled();

        expect(RobotEnvironmentStore.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotEnvironmentStore.data.serviceInstalled).toBe(true);
    });

    test(`env vars not set, UiPath.Service.UserHost.exe exists => Service not installed`, () => {
        const mock: IEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
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
        RobotEnvironmentStore.initialize({
            environment: mock,
            filenameUserHostService: 'UiPath.Service.UserHost.exe',
            relpathFailoverServiceHome: '..\\..'
        });

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).toHaveBeenCalled();

        expect(RobotEnvironmentStore.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotEnvironmentStore.data.serviceInstalled).toBe(false);
    });

    test(`env vars set but inconsistent, UiPath.Service.UserHost.exe exists => Service not installed`, () => {
        const mock: IEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
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
        RobotEnvironmentStore.initialize({
            environment: mock,
            envvarnameServiceInstalled: 'foo',
            filenameUserHostService: 'UiPath.Service.UserHost.exe',
            relpathFailoverServiceHome: '..\\..'
        });

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).toHaveBeenCalled();

        expect(RobotEnvironmentStore.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotEnvironmentStore.data.serviceInstalled).toBe(false);
    });

    test(`env vars set but inconsistent, UiPath.Service.UserHost.exe doesn't exist => Installed Service`, () => {
        const mock: IEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
            getVar: jest.fn(x => {
                switch (x) {
                    case 'foo': return 'neither true nor false';
                }
            }),
            fileExists: jest.fn(),
        };
        RobotEnvironmentStore.initialize({
            environment: mock,
            envvarnameServiceInstalled: 'foo',
            filenameUserHostService: 'UiPath.Service.UserHost.exe',
            relpathFailoverServiceHome: '..\\..'
        });

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).toHaveBeenCalled();

        expect(RobotEnvironmentStore.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotEnvironmentStore.data.serviceInstalled).toBe(true);
    });

    test(`UiPath.Service.UserHost.exe exists BUT env var states service is installed => Installed Service`, () => {
        const mock: IEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
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
        RobotEnvironmentStore.initialize({
            environment: mock,
            envvarnameServiceInstalled: 'foo',
            filenameUserHostService: 'UiPath.Service.UserHost.exe',
            relpathFailoverServiceHome: '..\\..'
        });

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).not.toHaveBeenCalled();

        expect(RobotEnvironmentStore.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotEnvironmentStore.data.serviceInstalled).toBe(true);
    });

    test(`UiPath.Service.UserHost.exe doesn't exist BUT env var states service is not installed => Service not installed`, () => {
        const mock: IEnvironment = {
            cwd: 'C:\\Program Files (x86)\\UiPath\\Agent\\Electron',
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
        RobotEnvironmentStore.initialize({
            environment: mock,
            envvarnameServiceInstalled: 'foo',
            filenameUserHostService: 'UiPath.Service.UserHost.exe',
            relpathFailoverServiceHome: '..\\..'
        });

        expect(mock.getVar).toHaveBeenCalled();
        expect(mock.fileExists).not.toHaveBeenCalled();

        expect(RobotEnvironmentStore.data.serviceHome).toBe('C:\\Program Files (x86)\\UiPath');
        expect(RobotEnvironmentStore.data.serviceInstalled).toBe(false);
    });

});
