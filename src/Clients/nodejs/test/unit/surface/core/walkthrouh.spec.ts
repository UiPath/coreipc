// import { Ipc, Config, coreIpcContract } from '@core';
// import { TimeSpan, Timeout } from '@foundation';
// import { expect } from 'chai';
// import { asParamsOf } from 'helpers';

// describe(`surface`, () => {
//     describe(`walkthrough`, () => {
//         context(`simple`, () => {

//             it(`should work`, async () => {

//                 @coreIpcContract
//                 class IMath {

//                 }
//             });

//             it(`should work 2`, async () => {
//                 class IService {
//                     public test(): Promise<void> { throw null; }
//                 }

//                 const store = new Config.Store();
//                 const root = new Ipc(store);

//                 const params = asParamsOf(store.readConfig);

//                 for (const _case of [
//                     { expected: false, args: params('allowImpersonation') },
//                     { expected: false, args: params('allowImpersonation', undefined, IService) },
//                     { expected: false, args: params('allowImpersonation', 'some pipe') },
//                     { expected: false, args: params('allowImpersonation', 'some pipe', IService) },

//                     { expected: Timeout.infiniteTimeSpan, args: params('requestTimeout') },
//                     { expected: Timeout.infiniteTimeSpan, args: params('requestTimeout', undefined, IService) },
//                     { expected: Timeout.infiniteTimeSpan, args: params('requestTimeout', 'some pipe') },
//                     { expected: Timeout.infiniteTimeSpan, args: params('requestTimeout', 'some pipe', IService) },

//                     { expected: undefined, args: params('connectHelper') },
//                     { expected: undefined, args: params('connectHelper', undefined, IService) },
//                     { expected: undefined, args: params('connectHelper', 'some pipe') },
//                     { expected: undefined, args: params('connectHelper', 'some pipe', IService) },
//                 ]) {
//                     expect(store.readConfig(..._case.args)).to.be.deep.eq(_case.expected);
//                 }

//                 root.config(builder => builder.allowImpersonation());

//                 for (const _case of [
//                     { expected: true, args: params('allowImpersonation') },
//                     { expected: true, args: params('allowImpersonation', undefined, IService) },
//                     { expected: true, args: params('allowImpersonation', 'some pipe') },
//                     { expected: true, args: params('allowImpersonation', 'some pipe', IService) },
//                 ]) {
//                     expect(store.readConfig(..._case.args)).to.be.deep.eq(_case.expected);
//                 }

//                 const helper = () => { };

//                 root.config(builder => builder.setConnectHelper(helper).setRequestTimeout(TimeSpan.fromDays(1)));

//                 for (const _case of [
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout') },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', undefined, IService) },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', 'some pipe') },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', 'some pipe', IService) },

//                     { expected: helper, args: params('connectHelper') },
//                     { expected: helper, args: params('connectHelper', undefined, IService) },
//                     { expected: helper, args: params('connectHelper', 'some pipe') },
//                     { expected: helper, args: params('connectHelper', 'some pipe', IService) },
//                 ]) {
//                     expect(store.readConfig(..._case.args)).to.be.deep.eq(_case.expected);
//                 }

//                 root.config('some pipe', builder => builder.setRequestTimeout(TimeSpan.fromSeconds(2)));

//                 for (const _case of [
//                     { expected: true, args: params('allowImpersonation') },
//                     { expected: true, args: params('allowImpersonation', undefined, IService) },
//                     { expected: true, args: params('allowImpersonation', 'some other pipe') },
//                     { expected: true, args: params('allowImpersonation', 'some other pipe', IService) },
//                     { expected: true, args: params('allowImpersonation', 'some pipe') },
//                     { expected: true, args: params('allowImpersonation', 'some pipe', IService) },

//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout') },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', undefined, IService) },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', 'some other pipe') },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', 'some other pipe', IService) },
//                     { expected: TimeSpan.fromSeconds(2), args: params('requestTimeout', 'some pipe') },
//                     { expected: TimeSpan.fromSeconds(2), args: params('requestTimeout', 'some pipe', IService) },

//                     { expected: helper, args: params('connectHelper') },
//                     { expected: helper, args: params('connectHelper', undefined, IService) },
//                     { expected: helper, args: params('connectHelper', 'some other pipe') },
//                     { expected: helper, args: params('connectHelper', 'some other pipe', IService) },
//                     { expected: helper, args: params('connectHelper', 'some pipe') },
//                     { expected: helper, args: params('connectHelper', 'some pipe', IService) },
//                 ]) {
//                     expect(store.readConfig(..._case.args)).to.be.deep.eq(_case.expected);
//                 }

//                 root.config('some pipe', IService, builder => builder.setRequestTimeout(TimeSpan.fromMilliseconds(10)));

//                 for (const _case of [
//                     { expected: true, args: params('allowImpersonation') },
//                     { expected: true, args: params('allowImpersonation', undefined, IService) },
//                     { expected: true, args: params('allowImpersonation', 'some other pipe') },
//                     { expected: true, args: params('allowImpersonation', 'some other pipe', IService) },
//                     { expected: true, args: params('allowImpersonation', 'some pipe') },
//                     { expected: true, args: params('allowImpersonation', 'some pipe', IService) },

//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout') },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', undefined, IService) },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', 'some other pipe') },
//                     { expected: TimeSpan.fromDays(1), args: params('requestTimeout', 'some other pipe', IService) },
//                     { expected: TimeSpan.fromSeconds(2), args: params('requestTimeout', 'some pipe') },
//                     { expected: TimeSpan.fromMilliseconds(10), args: params('requestTimeout', 'some pipe', IService) },

//                     { expected: helper, args: params('connectHelper') },
//                     { expected: helper, args: params('connectHelper', undefined, IService) },
//                     { expected: helper, args: params('connectHelper', 'some other pipe') },
//                     { expected: helper, args: params('connectHelper', 'some other pipe', IService) },
//                     { expected: helper, args: params('connectHelper', 'some pipe') },
//                     { expected: helper, args: params('connectHelper', 'some pipe', IService) },
//                 ]) {
//                     expect(store.readConfig(..._case.args)).to.be.deep.eq(_case.expected);
//                 }

//                 root.config(IService, builder => builder.setRequestTimeout(TimeSpan.zero));

//                 expect(store.readConfig('requestTimeout', undefined, IService)).to.be.deep.eq(TimeSpan.zero);
//                 expect(store.readConfig('requestTimeout', 'some other pipe', IService)).to.be.deep.eq(TimeSpan.zero);
//                 expect(store.readConfig('requestTimeout', 'some pipe', IService)).to.be.deep.eq(TimeSpan.fromMilliseconds(10));
//             });

//         });
//     });
// });
