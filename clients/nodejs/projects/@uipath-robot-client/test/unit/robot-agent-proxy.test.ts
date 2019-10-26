// tslint:disable: max-line-length
// tslint:disable: no-unused-expression

import { expect, spy, use, should } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import chaiAsPromised from 'chai-as-promised';

import { RobotAgentProxy } from '../../src/robot-agent-proxy';
import { RobotProxy, IRobotProxy } from '../../src/robot-proxy';
import { Observable, Observer, Subject } from 'rxjs';

import * as UpstreamContract from '../../src/upstream-contract';
import * as DownstreamContract from '../../src/downstream-contract';

use(spies);
use(chaiAsPromised);

describe(`class:RobotAgentProxy`, () => {
    function mockRobotProxy(partial: Partial<IRobotProxy>): IRobotProxy { return partial as any; }

    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            (() => new RobotAgentProxy()).should.not.throw();
            (() => new RobotAgentProxy(new RobotProxy())).should.not.throw();
        });
    });

    context(`method:CloseAsync`, () => {
        it(`shouldn't reject even if called multiple times`, async () => {
            const client = new RobotAgentProxy();
            await client.CloseAsync().should.eventually.be.fulfilled;
        });

        it(`should complete all observables exposed by RobotAgetProxy`, async () => {
            const client = new RobotAgentProxy();

            const observables = [
                client.RobotStatusChanged,
                client.ProcessListUpdated,
                client.JobStatusChanged,
                client.JobCompleted
            ];
            const _spies = observables.map((x: Observable<any>) => {
                const _spy = spy(() => {
                    console.log('foo');
                });
                x.subscribe({ complete: _spy });
                return _spy;
            });

            const promise = client.CloseAsync();
            await promise;
            for (const _spy of _spies) {
                _spy.should.have.been.called();
            }
        });
    });

    context(`property:RobotStatusChanged`, () => {
        it(`shouldn't throw`, () => {
            (() => new RobotAgentProxy().RobotStatusChanged).should.not.throw();
        });

        it(`should return an observable`, () => {
            expect(new RobotAgentProxy().RobotStatusChanged).to.be.instanceOf(Observable);
        });

        it(`shouldn't emit from the very beginning`, async () => {
            const mock = mockRobotProxy({
                ServiceUnavailable: new Subject<void>(),
                OrchestratorStatusChanged: new Subject<UpstreamContract.OrchestratorStatusChangedEventArgs>(),
                ProcessListChanged: new Subject<UpstreamContract.ProcessListChangedEventArgs>(),

                JobCompleted: new Subject<UpstreamContract.JobCompletedEventArgs>(),
                JobStatusChanged: new Subject<UpstreamContract.JobStatusChangedEventArgs>()
            });

            const client = new RobotAgentProxy(mock);
            let observer: Observer<DownstreamContract.RobotStatusChangedEventArgs> = null as any;
            client.RobotStatusChanged.subscribe(observer = {
                next: spy(() => { }),
                error: spy(() => { }),
                complete: spy(() => { })
            });

            await Promise.yield();

            observer.next.should.not.have.been.called();
            observer.error.should.not.have.been.called();
            observer.complete.should.not.have.been.called();
        });
    });
});
