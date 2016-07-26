#!/bin/sh
if test "$OS" = "Windows_NT"
then
  # use .Net

  .paket/paket.bootstrapper.exe
  exit_code=$?
  if [ $exit_code -ne 0 ]; then
    exit $exit_code
  fi

  .paket/paket.exe restore
  exit_code=$?
  if [ $exit_code -ne 0 ]; then
    exit $exit_code
  fi

  packages/FAKE/tools/FAKE.exe $@ --fsiargs build.fsx
else
  mono packages/FAKE/tools/FAKE.exe $@ --fsiargs -d:MONO build.fsx
fi
