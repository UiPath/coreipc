// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import { SerializationPal } from '../../../src/core/internals/serialization-pal';

import * as BrokerMessage from '../../../src/core/internals/broker-message';
import * as WireMessage from '../../../src/core/internals/wire-message';

import { ArgumentNullError, ArgumentError, InvalidOperationError } from '../../../src/foundation/errors';

use(spies);

describe(`core:internals -> class:SerializationPal`, () => {
    const _successfulNonVoidResultCases = [
        { returnedValue: null, providedSuffix: 'provided a returned value of null' },
        { returnedValue: 0, providedSuffix: 'provided a returned value of 0' },
        { returnedValue: 1, providedSuffix: 'provided a returned value of 1' },
        { returnedValue: false, providedSuffix: 'provided a returned value of false' },
        { returnedValue: true, providedSuffix: 'provided a returned value of true' },
        { returnedValue: '', providedSuffix: 'provided a returned value of ""' },
        { returnedValue: 'foo', providedSuffix: 'provided a returned value of "foo"' },
        { returnedValue: {}, providedSuffix: 'provided a returned value of {}' },
        { returnedValue: { x: 123, y: 'foo' }, providedSuffix: 'provided a returned value of { x: 123, y: "foo" }' },
        { returnedValue: [], providedSuffix: 'provided a returned value of []' },
        { returnedValue: [123, 'foo'], providedSuffix: 'provided a returned value of [123, "foo"]' }
    ];

    type NonNullable2<T> = {
        [P in keyof T]-?: NonNullable<T[P]>;
    };

    function setInner(error: Error, inner: Error): Error {
        (error as any).inner = inner;
        return error;
    }
    function setStackUndefined(error: Error): Error {
        error.stack = undefined;
        return error;
    }

    const _erroneousResultCases = [
        {
            error: new Error('root'),
            assert(x: NonNullable2<WireMessage.Error>) {
                expect(x.Type).not.to.be.null.and.not.to.be.undefined;
                expect(x.InnerError).to.be.oneOf([null, undefined]);

                x.Type.should.be.equal('Error');
                x.Message.should.be.equal('root');
            },
            assert2(x: Error) {
                x.should.be.instanceOf(Error);
                x.name.should.be.equal('Error');
                x.message.should.be.equal('root');
                x.should.not.have.property('inner');
            }
        },
        {
            error: new InvalidOperationError('root'),
            assert(x: NonNullable2<WireMessage.Error>) {
                expect(x.Type).not.to.be.null.and.not.to.be.undefined;
                expect(x.InnerError).to.be.oneOf([null, undefined]);

                x.Type.should.be.equal('InvalidOperationError');
                x.Message.should.be.equal('root');
            },
            assert2(x: Error) {
                x.should.be.instanceOf(Error);
                x.name.should.be.equal('InvalidOperationError');
                x.message.should.be.equal('root');
                x.should.not.have.property('inner');
            }
        },
        {
            error: setInner(new Error('root'), new Error('child')),
            assert(x: NonNullable2<WireMessage.Error>) {
                expect(x.Type).not.to.be.null.and.not.to.be.undefined;
                expect(x.InnerError).not.to.be.null.and.not.to.be.undefined;
                expect(x.InnerError.InnerError).to.be.oneOf([null, undefined]);

                x.Type.should.be.equal('Error');
                x.Message.should.be.equal('root');
                x.InnerError.Type.should.be.equal('Error');
                x.InnerError.Message.should.be.equal('child');
            },
            assert2(x: Error) {
                x.should.be.instanceOf(Error);
                x.name.should.be.equal('Error');
                x.message.should.be.equal('root');
                expect((x as any).inner).not.to.be.null.and.not.to.be.undefined;
                const inner = (x as any).inner as Error;
                inner.should.be.instanceOf(Error);
                inner.name.should.be.equal('Error');
                inner.message.should.be.equal('child');
            }
        },
        {
            error: setInner(new InvalidOperationError('root'), new InvalidOperationError('child')),
            assert(x: NonNullable2<WireMessage.Error>) {
                expect(x.Type).not.to.be.null.and.not.to.be.undefined;
                expect(x.InnerError).not.to.be.null.and.not.to.be.undefined;
                expect(x.InnerError.InnerError).to.be.oneOf([null, undefined]);

                x.Type.should.be.equal('InvalidOperationError');
                x.Message.should.be.equal('root');
                x.InnerError.Type.should.be.equal('InvalidOperationError');
                x.InnerError.Message.should.be.equal('child');
            },
            assert2(x: Error) {
                x.should.be.instanceOf(Error);
                x.name.should.be.equal('InvalidOperationError');
                x.message.should.be.equal('root');
                expect((x as any).inner).not.to.be.null.and.not.to.be.undefined;
                const inner = (x as any).inner as Error;
                inner.should.be.instanceOf(Error);
                inner.name.should.be.equal('InvalidOperationError');
                inner.message.should.be.equal('child');
            }
        },
        {
            error: setStackUndefined(new Error('root')),
            assert(x: NonNullable2<WireMessage.Error>) {
                expect(x.Type).not.to.be.null.and.not.to.be.undefined;
                expect(x.InnerError).to.be.oneOf([null, undefined]);

                x.Type.should.be.equal('Error');
                x.Message.should.be.equal('root');
            },
            assert2(x: Error) {
                x.should.be.instanceOf(Error);
                x.name.should.be.equal('Error');
                x.message.should.be.equal('root');
                x.should.not.have.property('inner');
            }
        }
    ];

    // context(`method:serializeResponse`, () => {
    //     it(`should throw provided a falsy BrokerMessage.Response`, () => {
    //         (() => SerializationPal.serializeResponse(null as any, '')).should.throw(ArgumentNullError).with.property('paramName', 'brokerResponse');
    //         (() => SerializationPal.serializeResponse(undefined as any, '')).should.throw(ArgumentNullError).with.property('paramName', 'brokerResponse');
    //     });

    //     it(`should throw provided a falsy id`, () => {
    //         (() => SerializationPal.serializeResponse({} as any, null as any)).should.throw(ArgumentNullError).with.property('paramName', 'id');
    //         (() => SerializationPal.serializeResponse({} as any, undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'id');
    //     });

    //     it(`shouldn't throw provided a truthy BrokerMessage.Response and id`, () => {
    //         const brokerResponse = new BrokerMessage.Response({}, null);
    //         const id = '123';
    //         (() => SerializationPal.serializeResponse(brokerResponse, id)).should.not.throw();
    //     });

    //     context(`the return buffer`, () => {
    //         context(`should not be null or undefined`, () => {
    //             for (const _case of _successfulNonVoidResultCases) {
    //                 it(_case.providedSuffix, () => {
    //                     const brokerResponse = new BrokerMessage.Response(_case.returnedValue, null);
    //                     const id = '123';
    //                     const buffer = SerializationPal.serializeResponse(brokerResponse, id);
    //                     expect(buffer).not.to.be.null.and.not.to.be.undefined;
    //                 });
    //             }
    //         });
    //         context(`should write 1 (meaning WireMessage.Type.Response) as the 1st byte`, () => {
    //             for (const _case of _successfulNonVoidResultCases) {
    //                 it(_case.providedSuffix, () => {
    //                     const brokerResponse = new BrokerMessage.Response(_case.returnedValue, null);
    //                     const id = '123';
    //                     const buffer = SerializationPal.serializeResponse(brokerResponse, id);
    //                     buffer.readUInt8(0).should.be.equal(WireMessage.Type.Response as number);
    //                 });
    //             }
    //         });
    //         context(`should write the payload's byte count as a Little Endian Int32 starting with the 2nd byte`, () => {
    //             for (const _case of _successfulNonVoidResultCases) {
    //                 it(_case.providedSuffix, () => {
    //                     const brokerResponse = new BrokerMessage.Response(_case.returnedValue, null);
    //                     const id = '123';
    //                     const buffer = SerializationPal.serializeResponse(brokerResponse, id);
    //                     buffer.readInt32LE(1).should.be.equal(buffer.length - 5);
    //                 });
    //             }
    //         });
    //         context(`should write the payload, starting with the 5th byte, as the UTF-8 encoding of the JSON serialization of the WireMessage.Response created from the BrokerMessage.Response`, () => {
    //             for (const _case of _successfulNonVoidResultCases) {
    //                 it(_case.providedSuffix, () => {
    //                     const brokerResponse = new BrokerMessage.Response(_case.returnedValue, null);
    //                     const id = '123';
    //                     const buffer = SerializationPal.serializeResponse(brokerResponse, id);
    //                     const json = buffer.subarray(5).toString('utf-8');
    //                     json.length.should.be.greaterThan(0);
    //                     let wireMessage: WireMessage.Response = null as any;
    //                     (() => wireMessage = JSON.parse(json)).should.not.throw();
    //                     expect(wireMessage).not.to.be.null;
    //                     expect(wireMessage.RequestId).to.be.equal('123');
    //                     expect(wireMessage.Error).to.be.null;
    //                     expect(wireMessage.Data).to.be.equal(JSON.stringify(_case.returnedValue));
    //                 });
    //             }
    //         });
    //         context(`should write the payload, starting with the 5th byte, as the UTF-8 encoding of the JSON serialization of the WireMessage.Response whose Data property is an empty string`, () => {
    //             it(`provided a void return (meaning undefined) value`, () => {
    //                 const brokerResponse = new BrokerMessage.Response(undefined, null);
    //                 const id = '123';
    //                 const buffer = SerializationPal.serializeResponse(brokerResponse, id);
    //                 const json = buffer.subarray(5).toString('utf-8');
    //                 json.length.should.be.greaterThan(0);
    //                 let wireMessage: WireMessage.Response = null as any;
    //                 (() => wireMessage = JSON.parse(json)).should.not.throw();
    //                 expect(wireMessage).not.to.be.null;
    //                 expect(wireMessage.RequestId).to.be.equal('123');
    //                 expect(wireMessage.Error).to.be.null;
    //                 expect(wireMessage.Data).to.be.equal('');
    //             });
    //         });

    //     });

    //     context(`it should write 1 byte with value 1 (meaning WireMessage.Type.Response) followed 4 bytes for the payload bytecount (UInt32 Little Endian) followed by the json payload`, () => {
    //         it(`should work provided a successful result`, () => {
    //             for (const _case of _successfulNonVoidResultCases) {
    //                 const brokerResponse = new BrokerMessage.Response(_case, null);
    //                 const id = '123';
    //                 const buffer = SerializationPal.serializeResponse(brokerResponse, id);

    //                 buffer.readUInt8(0).should.be.equal(WireMessage.Type.Response as number);
    //                 buffer.readUInt32LE(1).should.be.equal(buffer.length - 5);
    //                 const json = buffer.subarray(5).toString();

    //                 let wire: WireMessage.Response = null as any;
    //                 (() => wire = JSON.parse(json)).should.not.throw();

    //                 expect(wire).not.to.be.null;
    //                 expect(wire.Error).to.be.null;
    //                 expect(wire.RequestId).to.be.equal('123');
    //                 expect(wire.Data).not.to.be.null;

    //                 let obj: any = null;
    //                 (() => obj = JSON.parse(wire.Data as any)).should.not.throw();
    //                 expect(obj).to.deep.equal(_case);
    //             }
    //         });

    //         it(`should work provided an erroneous result`, () => {
    //             for (const _case of _erroneousResultCases) {
    //                 const brokerResponse = new BrokerMessage.Response(null, _case.error);
    //                 const id = '123';
    //                 const buffer = SerializationPal.serializeResponse(brokerResponse, id);

    //                 buffer.readUInt8(0).should.be.equal(WireMessage.Type.Response as number);
    //                 buffer.readUInt32LE(1).should.be.equal(buffer.length - 5);
    //                 const json = buffer.subarray(5).toString();

    //                 let wire: WireMessage.Response = null as any;
    //                 (() => wire = JSON.parse(json)).should.not.throw();

    //                 expect(wire).not.to.be.null;
    //                 expect(wire.Error).not.to.be.null;
    //                 expect(wire.RequestId).to.be.equal('123');
    //                 expect(wire.Data).to.be.null;

    //                 _case.assert(wire.Error as any);
    //             }
    //         });
    //     });

    // });

    context(`method:deserializeResponse`, () => {
        it(`should throw provided a falsy WireMessage`, () => {
            (() => SerializationPal.deserializeResponse(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'wireResponse');
            (() => SerializationPal.deserializeResponse(undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'wireResponse');
        });

        it(`shouldn't throw provided a truthy WireMessage`, () => {
            const wireResponse = new WireMessage.Response('id', '{}', null);
            (() => SerializationPal.deserializeResponse(wireResponse)).should.not.throw();
        });

        it(`should match the original successful BrokerMessage.Response`, () => {
            for (const _case of _successfulNonVoidResultCases) {
                const original = new BrokerMessage.Response(_case, null);

                const buffer = SerializationPal.wireResponseToBuffer(SerializationPal.brokerResponseToWireResponse(original, 'id'));
                const json = buffer.subarray(5).toString();
                const wireResponse = JSON.parse(json) as WireMessage.Response;
                const obj = SerializationPal.deserializeResponse(wireResponse);

                expect(obj.brokerResponse).not.to.be.null;
                expect(obj.brokerResponse).not.to.be.undefined;
                obj.brokerResponse.should.deep.equal(original);
            }
        });

        it(`should match the original erroneous BrokerMessage.Response`, () => {
            for (const _case of _erroneousResultCases) {
                const original = new BrokerMessage.Response(null, _case.error);

                const buffer = SerializationPal.wireResponseToBuffer(SerializationPal.brokerResponseToWireResponse(original, 'id'));
                const json = buffer.subarray(5).toString();
                const wireResponse = JSON.parse(json) as WireMessage.Response;
                const obj = SerializationPal.deserializeResponse(wireResponse);

                expect(obj.brokerResponse).not.to.be.null;
                expect(obj.brokerResponse).not.to.be.undefined;
                expect(obj.brokerResponse.maybeResult).to.be.null;
                expect(obj.brokerResponse.maybeError).not.to.be.null.and.not.to.be.undefined;

                _case.assert2(obj.brokerResponse.maybeError as any);
            }
        });
    });

    // context(`method:extract`, () => {
    //     it(`should throw provided a falsy request`, () => {
    //         (() => SerializationPal.extract(null as any, TimeSpan.fromMilliseconds(0))).should.throw(ArgumentNullError).with.property('paramName', 'request');
    //         (() => SerializationPal.extract(undefined as any, TimeSpan.fromMilliseconds(0))).should.throw(ArgumentNullError).with.property('paramName', 'request');
    //     });

    //     it(`should throw provided a falsy defaultTimeout`, () => {
    //         const request = new BrokerMessage.OutboundRequest('methodName', []);
    //         (() => SerializationPal.extract(request, null as any)).should.throw(ArgumentNullError).with.property('paramName', 'defaultTimeout');
    //         (() => SerializationPal.extract(request, undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'defaultTimeout');
    //     });

    //     it(`should throw provided a negative defaultTimeout`, () => {
    //         const request = new BrokerMessage.OutboundRequest('methodName', []);
    //         (() => SerializationPal.extract(request, TimeSpan.fromMilliseconds(-1))).should.throw(ArgumentError).with.property('paramName', 'defaultTimeout');
    //     });

    //     it(`shouldn't throw provided valid args`, () => {
    //         const request = new BrokerMessage.OutboundRequest('methodName', []);
    //         const defaultTimeout = TimeSpan.fromMilliseconds(10);

    //         (() => SerializationPal.extract(request, defaultTimeout)).should.not.throw();
    //         (() => SerializationPal.extract(request, defaultTimeout)).should.not.throw();
    //     });

    //     it(`should extract return the JSON serialization of the args in the BrokerMessage.Request, replacing the ct with null, return ct and the timeoutSeconds`, () => {
    //         const cts = new CancellationTokenSource();
    //         const ct = cts.token;
    //         const request = new BrokerMessage.OutboundRequest('methodName', [
    //             1,
    //             true,
    //             'foo',
    //             ct
    //         ]);
    //         const defaultTimeout = TimeSpan.fromDays(1);

    //         const obj = SerializationPal.extract(request, defaultTimeout);
    //         expect(obj.serializedArgs).to.be.deep.equal(['1', 'true', '"foo"', 'null']);
    //         expect(obj.timeoutSeconds).to.be.equal(86400);
    //         expect(obj.cancellationToken).not.to.be.null.and.not.to.be.undefined;

    //         obj.cancellationToken.isCancellationRequested.should.be.false;
    //         cts.cancel();
    //         obj.cancellationToken.isCancellationRequested.should.be.true;
    //     });

    //     it(`should allow a Message in the args list override the defaultTimeout`, () => {
    //         const defaultTimeout = TimeSpan.fromDays(1);
    //         const timeout = TimeSpan.fromDays(2);
    //         const request = new BrokerMessage.OutboundRequest('methodName', [new Message(timeout)]);

    //         let tuple: {
    //             serializedArgs: string[],
    //             timeoutSeconds: number,
    //             cancellationToken: CancellationToken,
    //             disposable: IDisposable
    //         } = null as any;
    //         try {
    //             tuple = SerializationPal.extract(request, defaultTimeout);
    //             expect(tuple.timeoutSeconds).to.be.equal(172800);
    //         } finally {
    //             if (tuple) {
    //                 tuple.disposable.dispose();
    //             }
    //         }
    //     });

    //     it(`shouldn't change the defaultTimeout when a Message with an undefined RequestTimeout is found`, () => {
    //         const defaultTimeout = TimeSpan.fromDays(1);
    //         const request = new BrokerMessage.OutboundRequest('methodName', [new Message()]);

    //         let tuple: {
    //             serializedArgs: string[],
    //             timeoutSeconds: number,
    //             cancellationToken: CancellationToken,
    //             disposable: IDisposable
    //         } = null as any;
    //         try {
    //             tuple = SerializationPal.extract(request, defaultTimeout);
    //             expect(tuple.timeoutSeconds).to.be.equal(86400);
    //         } finally {
    //             if (tuple) {
    //                 tuple.disposable.dispose();
    //             }
    //         }
    //     });
    // });

    // context(`method:serializeRequest`, () => {
    //     it(`should throw provided a null or undefined id`, () => {
    //         (() => SerializationPal.serializeRequest(null as any, 'methodName', [], 1)).should.throw(ArgumentNullError).with.property('paramName', 'id');
    //         (() => SerializationPal.serializeRequest(undefined as any, 'methodName', [], 1)).should.throw(ArgumentNullError).with.property('paramName', 'id');
    //         (() => SerializationPal.serializeRequest('' as any, 'methodName', [], 1)).should.throw(ArgumentNullError).with.property('paramName', 'id');
    //     });
    //     it(`should throw provided a falsy methodName`, () => {
    //         (() => SerializationPal.serializeRequest('id', null as any, [], 1)).should.throw(ArgumentNullError).with.property('paramName', 'methodName');
    //         (() => SerializationPal.serializeRequest('id', undefined as any, [], 1)).should.throw(ArgumentNullError).with.property('paramName', 'methodName');
    //         (() => SerializationPal.serializeRequest('id', '' as any, [], 1)).should.throw(ArgumentNullError).with.property('paramName', 'methodName');
    //     });
    //     it(`should throw provided a falsy serializedArgs`, () => {
    //         (() => SerializationPal.serializeRequest('id', 'methodName', null as any, 1)).should.throw(ArgumentNullError).with.property('paramName', 'serializedArgs');
    //         (() => SerializationPal.serializeRequest('id', 'methodName', undefined as any, 1)).should.throw(ArgumentNullError).with.property('paramName', 'serializedArgs');
    //     });
    //     it(`should throw provided a null or undefined timeoutSeconds`, () => {
    //         (() => SerializationPal.serializeRequest('id', 'methodName', [], null as any)).should.throw(ArgumentNullError).with.property('paramName', 'timeoutSeconds');
    //         (() => SerializationPal.serializeRequest('id', 'methodName', [], undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'timeoutSeconds');
    //     });
    //     it(`shouldn't throw provided a timeoutSeconds of 0`, () => {
    //         (() => SerializationPal.serializeRequest('id', 'methodName', [], 0)).should.not.throw();
    //     });
    //     it(`should throw provided negative timeoutSeconds`, () => {
    //         (() => SerializationPal.serializeRequest('id', 'methodName', [], -1)).should.throw(ArgumentError).with.property('paramName', 'timeoutSeconds');
    //     });
    //     it(`should write a byte of 0 (meaning WireMessage.Type.Request) followed by a 4 bytes for the payload byte count followed by the utf-8 encoding of the json serialization of a WireMessage.Request containing the provided args`, () => {
    //         const buffer = SerializationPal.serializeRequest('id', 'methodName', ['{}', 'true', '123', '[]'], 5);
    //         buffer.length.should.be.greaterThan(5);

    //         buffer.readUInt8(0).should.be.equal(WireMessage.Type.Request as number);
    //         buffer.readInt32LE(1).should.be.equal(buffer.length - 5);

    //         const json = buffer.subarray(5).toString('utf-8');

    //         expect(json).not.to.be.null;
    //         json.length.should.be.greaterThan(0);

    //         let obj: WireMessage.Request = null as any;
    //         (() => obj = JSON.parse(json)).should.not.throw();

    //         expect(obj).not.to.be.null;
    //         expect(obj.Id).to.be.equal('id');
    //         expect(obj.MethodName).to.be.equal('methodName');
    //         expect(obj.Parameters).to.be.deep.equal(['{}', 'true', '123', '[]']);
    //         expect(obj.TimeoutInSeconds).to.be.equal(5);
    //     });
    // });

    context(`method:deserializeRequest`, () => {
        it(`should throw provided a falsy wireRequest`, () => {
            (() => SerializationPal.deserializeRequest(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'wireRequest');
        });

        it(`shouldn't throw provided a valid wireRequest`, () => {
            const wireRequest = new WireMessage.Request(1, 'id', 'methodName', []);
            (() => SerializationPal.deserializeRequest(wireRequest)).should.not.throw();
        });
    });

    context(`method:fromJson`, () => {
        it(`should throw provided a falsy json`, () => {
            (() => SerializationPal.fromJson(null as any, WireMessage.Type.Request)).should.throw(ArgumentNullError).with.property('paramName', 'json');
            (() => SerializationPal.fromJson(undefined as any, WireMessage.Type.Request)).should.throw(ArgumentNullError).with.property('paramName', 'json');
            (() => SerializationPal.fromJson('', WireMessage.Type.Request)).should.throw(ArgumentNullError).with.property('paramName', 'json');
        });

        it(`should throw provided a falsy type`, () => {
            (() => SerializationPal.fromJson('{}', null as any)).should.throw(ArgumentNullError).with.property('paramName', 'type');
            (() => SerializationPal.fromJson('{}', undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'type');
        });

        it(`should throw provided a type different from WireMessage.Type.Request or WireMessage.Type.Response`, () => {
            (() => SerializationPal.fromJson('{}', 2 as any)).should.throw(ArgumentError).with.property('paramName', 'type');
        });

        it(`should deserialize to a WireMessage.Request provided a type of WireMessage.Type.Request`, () => {
            SerializationPal.fromJson('{}', WireMessage.Type.Request).should.be.instanceOf(WireMessage.Request);
        });

        it(`should deserialize to a WireMessage.Response provided a type of WireMessage.Type.Response`, () => {
            SerializationPal.fromJson('{}', WireMessage.Type.Response).should.be.instanceOf(WireMessage.Response);
        });

        it(`should deserialize any error in the error linked list to WireMessage.Error`, () => {
            const obj = {
                Error: {
                    InnerError: {
                        InnerError: {
                        }
                    }
                }
            };
            const json = JSON.stringify(obj);

            const obj2 = SerializationPal.fromJson(json, WireMessage.Type.Response) as any;
            expect(obj2.Error).to.be.instanceOf(WireMessage.Error);
            expect(obj2.Error.InnerError).to.be.instanceOf(WireMessage.Error);
            expect(obj2.Error.InnerError.InnerError).to.be.instanceOf(WireMessage.Error);
        });
    });
});
